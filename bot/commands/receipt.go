package commands

import (
	"errors"
	"github.com/drypa/ReceiptCollector/bot/backend"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
)

func getQr(update tgbotapi.Update) (string, error) {
	if update.Message.ReplyToMessage != nil {
		return update.Message.ReplyToMessage.Text, nil
	}
	return "", errors.New("need reply for QR request message")

}

func tryAddReceipt(userId string, messageText string, grpc *backend.GrpcClient) error {
	responseMessage, err := grpc.AddReceipt(getContext(), userId, messageText)
	if err != nil {
		return err
	}
	if responseMessage != "" {
		return errors.New(responseMessage)
	}
	return nil
}
