package markets

import (
	"context"
	"encoding/json"
	"github.com/gorilla/mux"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/bson/primitive"
	"go.mongodb.org/mongo-driver/mongo"
	"net/http"
	"os"
	"receipt_collector/mongo_client"
	"time"
)

type Market struct {
	Id   primitive.ObjectID `json:"id" bson:"_id,omitempty"`
	Name string             `json:"name"`
	Inns []string           `json:"inns"`
	Type MarketType         `json:"type"`
}

type MarketType string

const (
	Supermarket MarketType = "super_market"
	Fuel        MarketType = "fuel"
)

const mongoUrl = "mongodb://localhost:27017"

var mongoUser = os.Getenv("MONGO_ADMIN")
var mongoSecret = os.Getenv("MONGO_SECRET")

func MarketsBaseHandler(writer http.ResponseWriter, request *http.Request) {
	if request.Method == http.MethodGet {
		err := getMarketsHandler(writer, request)
		if err != nil {
			writer.WriteHeader(http.StatusInternalServerError)
		}
	}
	if request.Method == http.MethodPost {
		err := addMarketHandler(writer, request)
		if err != nil {
			writer.WriteHeader(http.StatusInternalServerError)
		}
	}

	writer.WriteHeader(http.StatusNotFound)
}

func ConcreteMarketHandler(writer http.ResponseWriter, request *http.Request) {
	if request.Method == http.MethodGet {
		market, err := getMarketHandler(writer, request)
		if err != nil {
			writer.WriteHeader(http.StatusInternalServerError)
			return
		}
		bytes, err := json.Marshal(market)
		if err != nil {
			writer.WriteHeader(http.StatusInternalServerError)
			return
		}
		writer.Write(bytes)
		return
	}
	if request.Method == http.MethodPut {
		err := updateMarketHandler(writer, request)
		if err != nil {
			writer.WriteHeader(http.StatusInternalServerError)
		}
		return
	}
}

func getMarketHandler(writer http.ResponseWriter, request *http.Request) (Market, error) {
	request.ParseForm()
	vars := mux.Vars(request)
	id := vars["id"]
	client, collection, ctx := getCollection()
	defer client.Disconnect(ctx)
	var market = Market{}
	objId, _ := primitive.ObjectIDFromHex(id)
	err := collection.FindOne(ctx, bson.M{"_id": objId}).Decode(&market)
	return market, err
}

func updateMarketHandler(writer http.ResponseWriter, request *http.Request) error {
	market := getMargetFromQuery(request)

	client, collection, ctx := getCollection()
	defer client.Disconnect(ctx)
	_, err := collection.UpdateOne(ctx, bson.M{"_id": market.Id}, market)
	return err
}

func getMarketsHandler(writer http.ResponseWriter, request *http.Request) error {
	defer request.Body.Close()

	markets := getMarkets()
	resp, err := json.Marshal(markets)
	if err != nil {
		return err
	}
	_, err = writer.Write(resp)
	return err
}

func addMarketHandler(writer http.ResponseWriter, request *http.Request) error {
	defer request.Body.Close()
	market := getMargetFromQuery(request)
	return insertNewMarket(market)
}
func insertNewMarket(market Market) error {
	client, collection, ctx := getCollection()
	defer client.Disconnect(ctx)
	_, err := collection.InsertOne(ctx, market)
	return err
}

func getCollection() (client *mongo.Client, collection *mongo.Collection, ctx context.Context) {
	ctx, _ = context.WithTimeout(context.Background(), 10*time.Second)
	client = mongo_client.GetMongoClient(mongoUrl, mongoUser, mongoSecret)
	collection = client.Database("receipt_collection").Collection("markets")
	return
}

func getMargetFromQuery(request *http.Request) Market {
	var market = Market{}
	json.NewDecoder(request.Body).Decode(&market)
	return market
}

func getMarkets() []Market {
	client, collection, ctx := getCollection()
	defer client.Disconnect(ctx)
	cursor, err := collection.Find(ctx, bson.D{})
	check(err)
	defer cursor.Close(ctx)
	var receipts = readMarkets(cursor, ctx)
	return receipts
}

func readMarkets(cursor *mongo.Cursor, context context.Context) []Market {
	var receipts = make([]Market, 0, 0)
	for cursor.Next(context) {
		var receipt Market
		err := cursor.Decode(&receipt)
		check(err)
		receipts = append(receipts, receipt)
	}
	return receipts
}

func check(err error) {
	if err != nil {
		panic(err)
	}
}
