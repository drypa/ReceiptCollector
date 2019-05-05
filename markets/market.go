package markets

import (
	"context"
	"encoding/json"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
	"net/http"
	"os"
	mongo_client "receipt_collector/mongo_client"
	"time"
)

type Market struct {
	Id   string     `json:"id" bson:"_id,omitempty"`
	Name string     `json:"name"`
	Inns []string   `json:"inns"`
	Type MarketType `json:"type"`
}

type MarketType string

const (
	Supermarket = "super_market"
	Fuel        = "fuel"
)

const mongoUrl = "mongodb://localhost:27017"

var mongoUser = os.Getenv("MONGO_ADMIN")
var mongoSecret = os.Getenv("MONGO_SECRET")

func GetMarketsHandler(writer http.ResponseWriter, request *http.Request) {
	if request.Method != http.MethodGet {
		writer.WriteHeader(http.StatusNotFound)
		return
	}

	defer request.Body.Close()
	markets := getMarkets()
	resp, err := json.Marshal(markets)
	check(err)
	_, err = writer.Write(resp)
	check(err)
}

func getMarkets() []Market {
	ctx, _ := context.WithTimeout(context.Background(), 10*time.Second)
	client := mongo_client.GetMongoClient(mongoUrl, mongoUser, mongoSecret)
	defer client.Disconnect(ctx)
	collection := client.Database("receipt_collection").Collection("markets")
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
