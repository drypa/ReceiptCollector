package commands

import (
	"errors"
	"github.com/drypa/ReceiptCollector/bot/backend"
	"github.com/drypa/ReceiptCollector/bot/backend/user"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
)

func AddReceipt(update tgbotapi.Update, bot *tgbotapi.BotAPI, provider user.Provider, grpcClient *backend.GrpcClient) error {
	id, err := provider.GetUserId(update.Message.From.ID)
	if err == nil {
		err = tryAddReceipt(id, update.Message.Text, grpcClient)
	}
	responseText := "Added"
	if err != nil {
		responseText = err.Error()
	}
	_, err = replyToMessage(update.Message.Chat.ID, bot, responseText, update.Message.MessageID)
	return err
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
