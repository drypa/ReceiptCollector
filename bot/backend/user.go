package backend

import (
	"errors"
	"net/http"
)

func (client Client) Register(userId int) error {
	registerUrl := client.backendUrl + "/api/account"
	request := registrationRequest{TelegramId: userId}
	reader, err := getReader(request)
	if err != nil {
		return err
	}
	response, err := http.Post(registerUrl, "text/javascript", reader)
	if err != nil {
		return err
	}
	switch response.StatusCode {
	case http.StatusOK:
		return nil
	default:
		return errors.New(response.Status)

	}
}
