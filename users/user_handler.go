package users

import (
	"context"
	"encoding/json"
	"errors"
	"go.mongodb.org/mongo-driver/mongo"
	"net/http"
	"os"
	"receipt_collector/mongo_client"
	"receipt_collector/passwords"
	"time"
)

const mongoUrl = "mongodb://localhost:27017"

var mongoUser = os.Getenv("MONGO_ADMIN")
var mongoSecret = os.Getenv("MONGO_SECRET")

func UserRegistrationHandler(writer http.ResponseWriter, request *http.Request) {
	defer request.Body.Close()
	ctx, _ := context.WithTimeout(request.Context(), 10*time.Second)
	if request.Method == http.MethodPost {
		registrationRequest, err := getUserRequestFromQuery(request)
		if err != nil {
			writer.WriteHeader(http.StatusInternalServerError)
		}
		user, err := mapToUser(registrationRequest)
		if err != nil {
			writer.WriteHeader(http.StatusInternalServerError)
		}
		err = insertNewUser(ctx, user)
		if err != nil {
			writer.WriteHeader(http.StatusInternalServerError)
		}
	}
}

func LoginHandler(writer http.ResponseWriter, request *http.Request) {
	//Do nothing
}

func mapToUser(registrationRequest UserRequest) (User, error) {
	hash, err := passwords.HashPassword(registrationRequest.Password)
	return User{Name: registrationRequest.Login, PasswordHash: hash}, err
}

func insertNewUser(ctx context.Context, user User) error {
	client, collection := getCollection()
	defer client.Disconnect(ctx)
	_, err := collection.InsertOne(ctx, user)
	return err
}

func getCollection() (client *mongo.Client, collection *mongo.Collection) {
	client, err := mongo_client.GetMongoClient(mongoUrl, mongoUser, mongoSecret)
	check(err)
	collection = client.Database("receipt_collection").Collection("system_users")
	return
}

func check(err error) {
	if err != nil {
		panic(err)
	}
}

func validateRequest(registrationRequest *UserRequest) error {
	if registrationRequest.Password == "" {
		return errors.New("no password found")
	}
	if registrationRequest.Login == "" {
		return errors.New("name is not specified")
	}
	return nil
}

func getUserRequestFromQuery(request *http.Request) (UserRequest, error) {
	var registrationRequest = UserRequest{}
	err := json.NewDecoder(request.Body).Decode(&registrationRequest)
	if err != nil {
		return registrationRequest, err
	}
	return registrationRequest, validateRequest(&registrationRequest)
}
