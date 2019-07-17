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
		registrationRequest, err := getUserRegistrationRequestFromQuery(request)
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
		return
	}
}
func mapToUser(registrationRequest RegistrationRequest) (User, error) {
	hash, err := passwords.HashPassword(registrationRequest.Password)
	return User{Name: registrationRequest.Name, PasswordHash: hash}, err
}

func insertNewUser(ctx context.Context, user User) error {
	client, collection := getCollection()
	defer client.Disconnect(ctx)
	_, err := collection.InsertOne(ctx, user)
	return err
}

func getCollection() (client *mongo.Client, collection *mongo.Collection) {
	client = mongo_client.GetMongoClient(mongoUrl, mongoUser, mongoSecret)
	collection = client.Database("receipt_collection").Collection("system_users")
	return
}

func validateRequest(registrationRequest *RegistrationRequest) error {
	if registrationRequest.Password == "" {
		return errors.New("no password found")
	}
	if registrationRequest.Name == "" {
		return errors.New("name is not specified")
	}
	return nil
}

func getUserRegistrationRequestFromQuery(request *http.Request) (RegistrationRequest, error) {
	var registrationRequest = RegistrationRequest{}
	err := json.NewDecoder(request.Body).Decode(&registrationRequest)
	if err != nil {
		return registrationRequest, err
	}
	return registrationRequest, validateRequest(&registrationRequest)
}
