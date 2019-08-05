package auth

import (
	"context"
	"fmt"
	"github.com/goji/httpauth"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
	"net/http"
	"os"
	"receipt_collector/mongo_client"
	"receipt_collector/passwords"
	"receipt_collector/users"
)

var mongoUrl = os.Getenv("MONGO_URL")
var mongoUser = os.Getenv("MONGO_LOGIN")
var mongoSecret = os.Getenv("MONGO_SECRET")

var authOpts = httpauth.AuthOptions{
	Realm:    "ReceiptCollection",
	AuthFunc: authFunc,
}

func authFunc(login string, password string, request *http.Request) bool {
	ctx := request.Context()
	client, collection := getCollection()
	err := client.Connect(ctx)
	if err != nil {
		return false
	}

	defer func() {
		err := client.Disconnect(ctx)
		if err != nil {
			fmt.Printf("error while mongo connection close %s", err)
		}
	}()
	var user users.User
	err = collection.FindOne(ctx, bson.D{{"name", login}}).Decode(&user)
	if err != nil {
		return false
	}

	isPasswordValid := passwords.ComparePasswordWithHash(user.PasswordHash, password)
	if isPasswordValid {
		newContext := context.WithValue(ctx, "userId", user.Id)
		request.WithContext(newContext)
	}
	return isPasswordValid
}

func RequireBasicAuth(router http.Handler) http.Handler {
	return httpauth.BasicAuth(authOpts)(router)
}

func getCollection() (client *mongo.Client, collection *mongo.Collection) {
	client = mongo_client.GetMongoClient(mongoUrl, mongoUser, mongoSecret)
	collection = client.Database("receipt_collection").Collection("system_users")
	return
}
