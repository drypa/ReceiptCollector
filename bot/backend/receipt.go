package backend

import (
	"errors"
	"net/http"
)

//AddReceipt adds receipt for telegram user.
func (client Client) AddReceipt(userId int, text string) error {
	addReceiptUrl := client.backendUrl + "/internal/receipt"
	request := addReceiptRequest{ReceiptString: text, TelegramId: userId}

	reader, err := getReader(request)
	if err != nil {
		return err
	}
	response, err := http.Post(addReceiptUrl, "text/javascript", reader)
	if err != nil {
		return err
	}
	switch response.StatusCode {
	case http.StatusOK:
		return nil
	default:
		return errors.New(response.Status)
	}
	return nil
}
