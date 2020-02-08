package users

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"receipt_collector/passwords"
	"time"
)

type Controller struct {
	repository Repository
}

func New(repository Repository) Controller {
	return Controller{
		repository: repository,
	}
}

func (controller Controller) UserRegistrationHandler(writer http.ResponseWriter, request *http.Request) {
	defer request.Body.Close()
	ctx, _ := context.WithTimeout(request.Context(), 10*time.Second)
	if request.Method == http.MethodPost {
		registrationRequest, err := getUserRequestFromQuery(request)
		if err != nil {
			onError(writer, err)
			return
		}
		user, err := mapToUser(registrationRequest)
		if err != nil {
			onError(writer, err)
			return
		}
		err = controller.repository.Insert(ctx, user)
		if err != nil {
			onError(writer, err)
			return
		}
	}
}

func onError(writer http.ResponseWriter, err error) {
	_ = fmt.Errorf("Error: %v", err)
	writer.WriteHeader(http.StatusInternalServerError)
}

func (controller Controller) LoginHandler(writer http.ResponseWriter, request *http.Request) {
	//Do nothing
}

func (controller Controller) RegisterHandler(writer http.ResponseWriter, request *http.Request) {
	ctx := request.Context()
	telegramId, err := getFromBody(request)
	if err != nil {
		onError(writer, err)
		return
	}
	user, err := controller.repository.GetByTelegramId(ctx, telegramId)
	if err != nil {
		onError(writer, err)
	}
	if user == nil {
		newUser := User{
			TelegramId: telegramId,
		}
		err := controller.repository.Insert(ctx, newUser)
		if err != nil {
			onError(writer, err)
		}
	}
}

func writeResponse(responseObject interface{}, writer http.ResponseWriter) {
	resp, err := json.Marshal(responseObject)
	if err != nil {
		onError(writer, err)
		return
	}
	_, err = writer.Write(resp)
	if err != nil {
		onError(writer, err)
		return
	}
}
func getFromBody(request *http.Request) (string, error) {
	registrationRequest := registrationRequest{}
	err := json.NewDecoder(request.Body).Decode(&registrationRequest)
	if err != nil {
		return "", err
	}
	return registrationRequest.TelegramId, nil
}

func mapToUser(registrationRequest UserRequest) (User, error) {
	hash, err := passwords.HashPassword(registrationRequest.Password)
	return User{Name: registrationRequest.Login, PasswordHash: hash}, err
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
