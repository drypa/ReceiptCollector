package main

import (
	"context"
	"github.com/gorilla/mux"
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
	"receipt_collector/users"
	"receipt_collector/users/login_url"
	"receipt_collector/workers"
	"time"
)

var baseAddress = os.Getenv("NALOGRU_BASE_ADDR")

var mongoURL = os.Getenv("MONGO_URL")
var mongoUser = os.Getenv("MONGO_LOGIN")
var mongoSecret = os.Getenv("MONGO_SECRET")
var openUrl = os.Getenv("OPEN_URL")

func main() {
	log.SetOutput(os.Stdout)
	settings := workers.ReadFromEnvironment()
	log.Printf("Worker settings %v \n", settings)

	ctx := context.Background()
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
		log.Println("Failed to create device service")
		return
	}

	d, err := deviceService.Rent(ctx)
	if err != nil {
		log.Println("Failed to rent device")
		return
	}

	nalogruClient := nalogru.NewClient(baseAddress, d)
	receiptRepository := receipts.NewRepository(client)
	userRepository := users.NewRepository(client)
	marketRepository := markets.NewRepository(client)

	worker := workers.New(nalogruClient, receiptRepository, deviceRepository, deviceService)

	go worker.CheckReceiptStart(ctx, settings)
	go worker.GetReceiptStart(ctx, settings)
	generator := login_url.New(openUrl)

	creds, err := credentials.NewServerTLSFromFile("/usr/share/receipts/ssl/certs/certificate.pem", "/usr/share/receipts/ssl/certs/private.key")
	if err != nil {
		log.Fatalf("failed to load TLS keys: %v", err)
	}
	var processor internal.Processor = login_url.NewLoginLinkProcessor(&userRepository, generator)

	go internal.Serve(":15000", creds, &processor)

	server := startServer(nalogruClient, receiptRepository, userRepository, marketRepository, deviceService)
	sigChan := make(chan os.Signal)
	signal.Notify(sigChan, os.Kill)
	signal.Notify(sigChan, os.Interrupt)

	sig := <-sigChan

	log.Printf("Service is shutting down... %s\n,", sig)
	ctx, cancel := context.WithTimeout(ctx, 10*time.Second)
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

func startServer(nalogruClient *nalogru.Client, receiptRepository receipts.Repository, userRepository users.Repository, marketRepository markets.Repository, devices nalogru.Devices) *http.Server {

	marketsController := markets.New(marketRepository)
	deviceController := controller.NewController(devices)

	receiptsController := receipts.New(receiptRepository, nalogruClient)
	usersController := users.New(userRepository)
	basicAuth := auth.New(userRepository)
	router := mux.NewRouter()
	registerUnauthenticatedRoutes(router, usersController, receiptsController)

	router.HandleFunc("/api/market", marketsController.MarketsBaseHandler)
	router.HandleFunc("/api/market/{id:[a-zA-Z0-9]+}", marketsController.ConcreteMarketHandler).Methods(http.MethodPut, http.MethodGet, http.MethodDelete)
	router.HandleFunc("/api/receipt", receiptsController.GetReceiptsHandler).Methods(http.MethodGet)
	router.HandleFunc("/api/receipt/{id:[a-zA-Z0-9]+}", receiptsController.GetReceiptDetailsHandler).Methods(http.MethodGet)
	router.HandleFunc("/api/receipt/{id:[a-zA-Z0-9]+}", receiptsController.DeleteReceiptHandler).Methods(http.MethodDelete)
	router.HandleFunc("/api/receipt/from-bar-code", receiptsController.AddReceiptHandler).Methods(http.MethodPost)
	router.HandleFunc("/api/receipt/batch", receiptsController.BatchAddReceiptHandler).Methods(http.MethodPost)

	router.HandleFunc("/api/device", deviceController.AddDeviceHandler).Methods(http.MethodPost)

	loginRoute := "/api/login"
	router.HandleFunc(loginRoute, usersController.LoginHandler).Methods(http.MethodPost)
	http.Handle("/", basicAuth.RequireBasicAuth(router))
	address := ":8888"
	log.Printf("Starting http server at: \"%s\"...", address)
	s := &http.Server{
		Addr:    address,
		Handler: router,
	}
	go func() {
		err := s.ListenAndServe()
		if err != nil {
			log.Fatal(err)
		}
	}()

	return s
}

func registerUnauthenticatedRoutes(router *mux.Router, usersController users.Controller, receiptsController receipts.Controller) {
	registrationRoute := "/api/user/register"
	router.HandleFunc(registrationRoute, usersController.UserRegistrationHandler).Methods(http.MethodPost)

	registrationByTelegramRoute := "/internal/account"
	router.HandleFunc(registrationByTelegramRoute, usersController.GetUserByTelegramIdHandler).Methods(http.MethodPost)

	getUsersRoute := "/internal/account"
	router.HandleFunc(getUsersRoute, usersController.GetUsersHandler).Methods(http.MethodGet)

	addReceiptRoute := "/internal/receipt"
	router.HandleFunc(addReceiptRoute, receiptsController.AddReceiptForTelegramUserHandler).Methods(http.MethodPost)

	loginByLinkRoute := "/api/auth/link/{id:[a-zA-Z0-9]+}"
	router.HandleFunc(loginByLinkRoute, usersController.LoginByLinkHandler)

	http.Handle(registrationRoute, router)
	http.Handle("/internal/", router)
	http.Handle("/api/auth/link/", router)
}

func check(err error) {
	if err != nil {
		log.Printf("Error occurred %v", err)
		panic(err)
	}
}
