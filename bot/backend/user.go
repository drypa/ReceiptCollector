package backend

import (
	"errors"
	"net/http"
	"path"
)

func (client Client) register(userId string) error {
	registerUrl := path.Join(client.backendUrl, "/api/account")
	reader, err := getReader(registrationRequest{telegramId: userId})
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
