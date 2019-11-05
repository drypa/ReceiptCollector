package users

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"go.mongodb.org/mongo-driver/mongo"
	"net/http"
	"receipt_collector/mongo_client"
	"receipt_collector/passwords"
	"time"
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

func (controller Controller) UserRegistrationHandler(writer http.ResponseWriter, request *http.Request) {
	defer request.Body.Close()
	ctx, _ := context.WithTimeout(request.Context(), 10*time.Second)
	if request.Method == http.MethodPost {
		registrationRequest, err := getUserRequestFromQuery(request)
		if err != nil {
			OnError(writer, err)
			return
		}
		user, err := mapToUser(registrationRequest)
		if err != nil {
			OnError(writer, err)
			return
		}
		err = controller.insertNewUser(ctx, user)
		if err != nil {
			OnError(writer, err)
			return
		}
	}
}

func OnError(writer http.ResponseWriter, err error) {
	_ = fmt.Errorf("Error: %v", err)
	writer.WriteHeader(http.StatusInternalServerError)
}

func (controller Controller) LoginHandler(writer http.ResponseWriter, request *http.Request) {
	//Do nothing
}

func mapToUser(registrationRequest UserRequest) (User, error) {
	hash, err := passwords.HashPassword(registrationRequest.Password)
	return User{Name: registrationRequest.Login, PasswordHash: hash}, err
}

func (controller Controller) insertNewUser(ctx context.Context, user User) error {
	client, collection := controller.getCollection()
	defer client.Disconnect(ctx)
	_, err := collection.InsertOne(ctx, user)
	return err
}

func (controller Controller) getCollection() (client *mongo.Client, collection *mongo.Collection) {
	client, err := mongo_client.GetMongoClient(controller.mongoUrl, controller.mongoLogin, controller.mongoPassword)
	check(err)
	collection = client.Database("receipt_collection").Collection("system_users")
	return
}

func check(err error) {
	if err != nil {
		fmt.Errorf("Panic: %v", err)
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