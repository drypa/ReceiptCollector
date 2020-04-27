package users

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"receipt_collector/dispose"
	"receipt_collector/passwords"
	"time"
)

//Controller of Users.
type Controller struct {
	repository Repository
}

//New creates Controller instance.
func New(repository Repository) Controller {
	return Controller{
		repository: repository,
	}
}

//UserRegistrationHandler provides user registration.
func (controller Controller) UserRegistrationHandler(writer http.ResponseWriter, request *http.Request) {
	defer dispose.Dispose(request.Body.Close, "Error while request body close")
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
		err = controller.repository.Insert(ctx, &user)
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

//GetUserByTelegramIdHandler returns user by telegramId.
func (controller Controller) GetUserByTelegramIdHandler(writer http.ResponseWriter, request *http.Request) {
	ctx := request.Context()
	defer dispose.Dispose(request.Body.Close, "error while request body close")
	telegramId, err := getFromBody(request)
	if err != nil {
		onError(writer, err)
		return
	}
	user, err := controller.repository.GetByTelegramId(ctx, int(telegramId))
	if err != nil {
		onError(writer, err)
		return
	}
	if user != nil {
		response := mapToContract(*user)
		writeResponse(response, writer)
		return
	}
	newUser := User{
		TelegramId: int(telegramId),
	}
	err = controller.repository.Insert(ctx, &newUser)
	if err != nil {
		onError(writer, err)
		return
	}
	writeResponse(mapToContract(newUser), writer)
}

func mapToContract(model User) user {
	return user{
		UserId:     model.Id.Hex(),
		TelegramId: model.TelegramId,
	}
}
func mapToContractList(model []User) []user {
	res := make([]user, len(model))
	for i, v := range model {
		res[i] = mapToContract(v)
	}
	return res
}

//GetUsersHandler allow to get all users.
func (controller Controller) GetUsersHandler(writer http.ResponseWriter, request *http.Request) {
	ctx := request.Context()
	users, err := controller.repository.GetAll(ctx)
	if err != nil {
		onError(writer, err)
	}
	response := mapToContractList(users)
	writeResponse(response, writer)
}

//LoginByLinkHandler allow login through one-time link.
func (controller Controller) LoginByLinkHandler(writer http.ResponseWriter, request *http.Request) {
	writeResponse("response", writer)
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
func getFromBody(request *http.Request) (int32, error) {
	registrationRequest := registrationRequest{}
	err := json.NewDecoder(request.Body).Decode(&registrationRequest)
	if err != nil {
		return 0, err
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
