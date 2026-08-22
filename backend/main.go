package main

import (
	"context"
	"go.mongodb.org/mongo-driver/mongo"
	"google.golang.org/grpc/credentials"
	"log"
	"net/http"
	"os"
	"os/signal"
	"receipt_collector/auth"
	"receipt_collector/device"
	"receipt_collector/device/controller"
	"receipt_collector/device/repository"
	"receipt_collector/dispose"
	"receipt_collector/internal"
	"receipt_collector/markets"
	"receipt_collector/mongo_client"
	"receipt_collector/nalogru"
	"receipt_collector/receipts"
	"receipt_collector/render"
	"receipt_collector/reports"
	"receipt_collector/reports/dal"
	"receipt_collector/users"
	"receipt_collector/users/link"
	"receipt_collector/waste"
	"receipt_collector/workers"
	"time"
)

var baseAddress = os.Getenv("NALOGRU_BASE_ADDR")

var mongoURL = os.Getenv("MONGO_URL")
var mongoUser = os.Getenv("MONGO_LOGIN")
var mongoSecret = os.Getenv("MONGO_SECRET")
var openUrl = os.Getenv("OPEN_URL")
var templatePath = os.Getenv("TEMPLATES_PATH")
var clientSecret = os.Getenv("CLIENT_SECRET")

func main() {
	log.SetOutput(os.Stdout)
	settings := workers.ReadFromEnvironment()
	log.Printf("Worker settings %v \n", settings)

	ctx, cancelFunc := context.WithCancel(context.Background())
	client, err := getMongoClient()
	if err != nil {
		check(err)
	}
	defer dispose.Dispose(func() error {
		return client.Disconnect(context.Background())
	}, "error while mongo disconnect")
	deviceRepository := repository.NewRepository(client)
	deviceService, err := device.NewService(ctx, deviceRepository)
	if err != nil {
		log.Printf("Failed to create device service: %v\n", err)
		return
	}
	nalogruClient := nalogru.NewClient(baseAddress)
	receiptRepository := receipts.NewRepository(client)
	userRepository := users.NewRepository(client)
	marketRepository := markets.NewRepository(client)
	wasteRepository := waste.NewRepository(client)
	receiptReportRepository := dal.New(client)

	worker := workers.New(nalogruClient, receiptRepository, &wasteRepository, deviceService)

	//wasteWorker := waste.NewWorker()
	//go func() {
	//	var err = wasteWorker.Process(ctx, client)
	//	if err != nil {
	//		log.Fatal(err)
	//	}
	//}()

	// Create separate contexts for each worker with appropriate timeouts
	receiptCtx, receiptCancel := context.WithTimeout(ctx, 60*time.Second)
	go worker.GetReceiptStart(receiptCtx, settings)
	defer receiptCancel()

	// Electronic receipt worker runs once daily (long interval)
	eRecCtx, eRecCancel := context.WithTimeout(ctx, 60*time.Minute)
	worker.GetElectronicReceiptStart(eRecCtx)
	defer eRecCancel()

	creds, err := credentials.NewServerTLSFromFile("/usr/share/receipts/ssl/certs/certificate.crt", "/usr/share/receipts/ssl/certs/private.key")
	if err != nil {
		log.Fatalf("failed to load TLS keys: %v", err)
	}

	linkClient := link.NewClient(openUrl)
	var accountProcessor internal.AccountProcessor = users.NewProcessor(&userRepository, nalogruClient, deviceService, linkClient, clientSecret)
	r := render.New(templatePath)

	var receiptProcessor internal.ReceiptProcessor = receipts.NewProcessor(&receiptRepository, r)

	// gRPC listeners
	_, reportsCancel := context.WithTimeout(ctx, 60*time.Minute)
	go internal.Serve(":15000", creds, &accountProcessor, &receiptProcessor)
	go reports.Serve(":15001", creds, &userRepository, &receiptReportRepository)
	defer reportsCancel()

	server := startServer(receiptRepository, userRepository, marketRepository, wasteRepository, deviceService)

	sigChan := make(chan os.Signal)
	signal.Notify(sigChan, os.Kill)
	signal.Notify(sigChan, os.Interrupt)

	sig := <-sigChan

	log.Printf("Service is shutting down... %s\n,", sig)
	// Cancel all worker contexts in proper order (shortest to longest timeout)
	receiptCancel()
	reportsCancel()
	eRecCancel()
	cancelFunc()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	err = server.Shutdown(ctx)

	if err != nil {
		cancel()
		log.Fatal(err)
	}
}

func getMongoClient() (*mongo.Client, error) {
	settings := mongo_client.NewSettings(mongoURL, mongoUser, mongoSecret)
	return mongo_client.New(settings)
}

func startServer(receiptRepository receipts.Repository,
	userRepository users.Repository,
	marketRepository markets.Repository,
	wasteRepository waste.Repository,
	devices nalogru.Devices) *http.Server {
	marketsController := markets.New(marketRepository)
	deviceController := controller.NewController(devices)

	receiptsController := receipts.New(receiptRepository)
	usersController := users.New(userRepository)
	wasteController := waste.New(wasteRepository)
	basicAuth := auth.New(userRepository)
	handlers := apiHandlers{
		marketsBase:      marketsController.MarketsBaseHandler,
		concreteMarket:   marketsController.ConcreteMarketHandler,
		getReceipts:      receiptsController.GetReceiptsHandler,
		receiptDetails:   receiptsController.GetReceiptDetailsHandler,
		deleteReceipt:    receiptsController.DeleteReceiptHandler,
		addReceipt:       receiptsController.AddReceiptHandler,
		batchAddReceipt:  receiptsController.BatchAddReceiptHandler,
		addDevice:        deviceController.AddDeviceHandler,
		waste:            wasteController.GetHandler,
		login:            usersController.LoginHandler,
		userRegistration: usersController.UserRegistrationHandler,
		internalAccount:  usersController.GetUserByTelegramIdHandler,
		internalReceipt:  receiptsController.AddReceiptForTelegramUserHandler,
	}
	rootHandler := newRootHandler(basicAuth.RequireBasicAuth, handlers)
	address := ":8888"
	log.Printf("Starting http server at: \"%s\"...", address)
	s := &http.Server{
		Addr:    address,
		Handler: rootHandler,
	}
	go func() {
		err := s.ListenAndServe()
		if err != nil {
			log.Fatal(err)
		}
	}()

	return s
}

func check(err error) {
	if err != nil {
		log.Printf("Error occurred %v", err)
		panic(err)
	}
}
