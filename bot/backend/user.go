package backend

import (
	"encoding/json"
	"errors"
	"log"
	"net/http"
)

//GetUser returns user by telegram id.
func (client Client) GetUser(userId int) (User, error) {
	var u User
	registerUrl := client.backendUrl + "/internal/account"
	request := registrationRequest{TelegramId: userId}
	reader, err := getReader(request)
	if err != nil {
		return u, err
	}
	response, err := http.Post(registerUrl, "text/javascript", reader)
	if err != nil {
		log.Print("Could not parse backend response as User.")
		return u, err
	}
	switch response.StatusCode {
	case http.StatusOK:
		err := getResult(response, &u)
		return u, err
	default:
		return u, errors.New(response.Status)

	}
}

func getResult(response *http.Response, result interface{}) error {
	return json.NewDecoder(response.Body).Decode(result)
}

//GetUsers returns all users.
func (client Client) GetUsers() ([]User, error) {
	getUsersUrl := client.backendUrl + "/internal/account"
	response, err := http.Get(getUsersUrl)
	if err != nil {
		return nil, err
	}
	if response.StatusCode == http.StatusOK {
		var users []User
		err := getResult(response, &users)
		return users, err
	}
	return nil, errors.New(response.Status)

}
