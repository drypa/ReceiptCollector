package markets

import (
	"context"
	"encoding/json"
	"fmt"
	"github.com/gorilla/mux"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/bson/primitive"
	"go.mongodb.org/mongo-driver/mongo"
	"net/http"
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

type Controller struct {
	mongoUrl      string
	mongoLogin    string
	mongoPassword string
}

func New(mongoUrl string, mongoUser string, mongoSecret string) Controller {
	return Controller{
		mongoUrl:      mongoUrl,
		mongoLogin:    mongoUser,
		mongoPassword: mongoSecret,
	}
}

func (controller Controller) MarketsBaseHandler(writer http.ResponseWriter, request *http.Request) {
	if request.Method == http.MethodGet {
		err := controller.getMarketsHandler(writer, request)
		if err != nil {
			OnError(writer, err)
			return
		}
	}
	if request.Method == http.MethodPost {
		err := controller.addMarketHandler(writer, request)
		if err != nil {
			OnError(writer, err)
			return
		}
	}

	writer.WriteHeader(http.StatusNotFound)
}
func OnError(writer http.ResponseWriter, err error) {
	_ = fmt.Errorf("Error: %v", err)
	writer.WriteHeader(http.StatusInternalServerError)
}

func (controller Controller) ConcreteMarketHandler(writer http.ResponseWriter, request *http.Request) {
	if request.Method == http.MethodGet {
		market, err := controller.getMarketHandler(writer, request)
		if err != nil {
			OnError(writer, err)
			return
		}
		bytes, err := json.Marshal(market)
		if err != nil {
			OnError(writer, err)
			return
		}
		writer.Write(bytes)
		return
	}
	if request.Method == http.MethodPut {
		err := controller.updateMarketHandler(writer, request)
		if err != nil {
			OnError(writer, err)
			return
		}
		return
	}
}

func (controller Controller) getMarketHandler(writer http.ResponseWriter, request *http.Request) (Market, error) {
	request.ParseForm()
	vars := mux.Vars(request)
	id := vars["id"]
	client, collection, ctx := controller.getCollection()
	defer client.Disconnect(ctx)
	var market = Market{}
	objId, _ := primitive.ObjectIDFromHex(id)
	err := collection.FindOne(ctx, bson.M{"_id": objId}).Decode(&market)
	return market, err
}

func (controller Controller) updateMarketHandler(writer http.ResponseWriter, request *http.Request) error {
	market := controller.getMargetFromQuery(request)

	client, collection, ctx := controller.getCollection()
	defer client.Disconnect(ctx)
	_, err := collection.UpdateOne(ctx, bson.M{"_id": market.Id}, market)
	return err
}

func (controller Controller) getMarketsHandler(writer http.ResponseWriter, request *http.Request) error {
	defer request.Body.Close()

	markets := controller.getMarkets()
	resp, err := json.Marshal(markets)
	if err != nil {
		return err
	}
	_, err = writer.Write(resp)
	return err
}

func (controller Controller) addMarketHandler(writer http.ResponseWriter, request *http.Request) error {
	defer request.Body.Close()
	market := controller.getMargetFromQuery(request)
	return controller.insertNewMarket(market)
}
func (controller Controller) insertNewMarket(market Market) error {
	client, collection, ctx := controller.getCollection()
	defer client.Disconnect(ctx)
	_, err := collection.InsertOne(ctx, market)
	return err
}

func (controller Controller) getCollection() (client *mongo.Client, collection *mongo.Collection, ctx context.Context) {
	ctx, _ = context.WithTimeout(context.Background(), 10*time.Second)
	client, err := mongo_client.GetMongoClient(controller.mongoUrl, controller.mongoLogin, controller.mongoPassword)
	check(err)
	collection = client.Database("receipt_collection").Collection("markets")
	return
}

func (controller Controller) getMargetFromQuery(request *http.Request) Market {
	var market = Market{}
	json.NewDecoder(request.Body).Decode(&market)
	return market
}

func (controller Controller) getMarkets() []Market {
	client, collection, ctx := controller.getCollection()
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
		fmt.Errorf("Panic: %v", err)
		panic(err)
	}
}
