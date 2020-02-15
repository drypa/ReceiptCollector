package backend

import (
	"encoding/json"
	"errors"
	"net/http"
)

func (client Client) GetUser(userId int) (User, error) {
	u := User{}
	registerUrl := client.backendUrl + "/api/account"
	request := registrationRequest{TelegramId: userId}
	reader, err := getReader(request)
	if err != nil {
		return u, err
	}
	response, err := http.Post(registerUrl, "text/javascript", reader)
	if err != nil {
		return u, err
	}
	switch response.StatusCode {
	case http.StatusOK:
		err := getResult(response, u)
		return u, err
	default:
		return u, errors.New(response.Status)

	}
}

func getResult(response *http.Response, result interface{}) error {
	return json.NewDecoder(response.Body).Decode(result)
}

func (client Client) GetUsers() ([]User, error) {
	getUsersUrl := client.backendUrl + "/api/account"
	response, err := http.Get(getUsersUrl)
	if err != nil {
		return nil, err
	}
	if response.StatusCode == http.StatusOK {
		users := make([]User, 0)
		err := getResult(response, &users)
		return users, err
	}
	return nil, errors.New(response.Status)

}
