package backend

import (
	"encoding/json"
	"errors"
	"fmt"
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
		log.Print("User request error.")
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

//GetLoginLink returns URL for automatic login.
func (client Client) GetLoginLink(userId int) (string, error) {
	getLinkUrl := fmt.Sprintf("%s/internal/%d/login-link", client.backendUrl, userId)
	response, err := http.Get(getLinkUrl)
	if err != nil {
		log.Print("Get login link request error.")
		return "", err
	}
	if response.StatusCode == http.StatusFound {
		location := response.Header.Get("Location")
		return location, nil
	}
	return "", errors.New(response.Status)

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
