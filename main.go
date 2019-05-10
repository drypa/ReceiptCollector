package main

import (
	"context"
	"encoding/json"
	"fmt"
	"github.com/adjust/redismq"
	"github.com/google/uuid"
	"github.com/gorilla/mux"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
	"html/template"
	"io/ioutil"
	"net/http"
	"net/url"
	"os"
	"receipt_collector/markets"
	"receipt_collector/mongo_client"
	"receipt_collector/nalogru_client"
	"time"
)

var login = os.Getenv("NALOGRU_LOGIN")
var password = os.Getenv("NALOGRU_PASS")
var rawReceiptQueue = redismq.CreateQueue("localhost", "6379", "", 6, "raw-receipts")

const dumpDirectory = "./stub/dump/"
const baseAddress = "http://localhost:9999"
const mongoUrl = "mongodb://localhost:27017"

var mongoUser = os.Getenv("MONGO_ADMIN")
var mongoSecret = os.Getenv("MONGO_SECRET")

func main() {
	go consumeRawReceipts(rawReceiptQueue)
	router := mux.NewRouter()
	router.HandleFunc("/api/market", markets.MarketsBaseHandler)
	router.HandleFunc("/api/market/{id:[a-zA-Z0-9]+}", markets.ConcreteMarketHandler).Methods(http.MethodPut, http.MethodGet, http.MethodDelete)
	router.HandleFunc("/api/receipt", getReceiptHandler)
	router.HandleFunc("/api/receipt/as-query", addReceiptHandler)
	http.Handle("/", router)
	address := ":8888"
	fmt.Printf("Starting http server at: \"%s\"...", address)
	fmt.Println(http.ListenAndServe(address, nil))
}

func getReceiptHandler(writer http.ResponseWriter, request *http.Request) {
	if request.Method != http.MethodGet {
		writer.WriteHeader(http.StatusNotFound)
		return
	}
	ctx, _ := context.WithTimeout(context.Background(), 10*time.Second)
	defer request.Body.Close()
	client := mongo_client.GetMongoClient(mongoUrl, mongoUser, mongoSecret)
	defer client.Disconnect(ctx)
	collection := client.Database("receipt_collection").Collection("receipts")
	cursor, err := collection.Find(ctx, bson.D{})
	if err != nil {
		writer.WriteHeader(http.StatusInternalServerError)
		return
	}
	defer cursor.Close(ctx)
	var receipts = readReceipts(cursor, ctx)
	resp, err := json.Marshal(receipts)
	if err != nil {
		writer.WriteHeader(http.StatusInternalServerError)
		return
	}
	_, err = writer.Write(resp)
	if err != nil {
		writer.WriteHeader(http.StatusInternalServerError)
		return
	}
}

func readReceipts(cursor *mongo.Cursor, context context.Context) []Receipt {
	var receipts = make([]Receipt, 0, 0)
	for cursor.Next(context) {
		var receipt Receipt
		err := cursor.Decode(&receipt)
		check(err)
		receipts = append(receipts, receipt)
	}
	return receipts
}

func addReceiptHandler(writer http.ResponseWriter, request *http.Request) {
	if request.Method != http.MethodPost {
		writer.WriteHeader(http.StatusNotFound)
		return
	}
	defer request.Body.Close()
	err := request.ParseForm()
	if err != nil {
		writer.WriteHeader(http.StatusBadRequest)
	}
	receiptParams := parseQuery(&request.Form)
	fmt.Println(receiptParams)

	rawReceipt, err := nalogru_client.GetRawReceipt(baseAddress, receiptParams, login, password)
	check(err)
	dumpToFile(rawReceipt)
	saveResponse(rawReceiptQueue, rawReceipt)
}

func dumpToFile(rawReceipt []byte) {
	unique, _ := uuid.NewUUID()
	err := ioutil.WriteFile(dumpDirectory+unique.String()+".json", rawReceipt, 0644)
	check(err)
}

func saveResponse(queue *redismq.Queue, response []byte) {
	err := queue.Put(string(response))
	check(err)
}

func check(err error) {
	if err != nil {
		panic(err)
	}
}

func parseQuery(form *url.Values) nalogru_client.ParseResult {
	timeString := form.Get("t")

	timeParsed := parseAsTime(timeString)

	return nalogru_client.ParseResult{
		N:          template.HTMLEscapeString(form.Get("n")),
		FiscalSign: template.HTMLEscapeString(form.Get("fp")),
		Sum:        template.HTMLEscapeString(form.Get("s")),
		Fd:         template.HTMLEscapeString(form.Get("fn")),
		Time:       timeParsed,
		Fp:         template.HTMLEscapeString(form.Get("i")),
	}
}

func parseReceipt(bytes []byte) Receipt {
	var receipt map[string]map[string]Receipt
	err := json.Unmarshal(bytes, &receipt)
	check(err)

	res := receipt["document"]["receipt"]

	return res
}

func consumeRawReceipts(rawQueue *redismq.Queue) {
	consumer, err := rawQueue.AddConsumer("receipt-parser")
	check(err)
	defer consumer.Quit()
	ctx, _ := context.WithTimeout(context.Background(), 10*time.Second)
	client := mongo_client.GetMongoClient(mongoUrl, mongoUser, mongoSecret)
	defer func() {
		err := client.Disconnect(ctx)
		fmt.Printf("error while disconnect %s", err)
	}()
	collection := client.Database("receipt_collection").Collection("receipts")

	if consumer.HasUnacked() {
		unacked, err := consumer.GetUnacked()
		check(err)
		processReceipt(unacked, collection)
		err = unacked.Ack()
		check(err)
	}

	for {
		message, err := consumer.Get()
		check(err)

		processReceipt(message, collection)
		err = message.Ack()
		check(err)
	}
}

func processReceipt(message *redismq.Package, collection *mongo.Collection) {
	ctx, _ := context.WithTimeout(context.Background(), 10*time.Second)
	receipt := parseReceipt([]byte(message.Payload))
	fmt.Println(receipt.String())
	for i := 0; i < len(receipt.Items); i++ {
		fmt.Println(receipt.Items[i].String())
	}
	_, err := collection.InsertOne(ctx, receipt)
	check(err)

}
