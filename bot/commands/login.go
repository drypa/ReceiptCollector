package commands

import (
	"github.com/drypa/ReceiptCollector/bot/backend"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
)

func Login(update tgbotapi.Update, bot *tgbotapi.BotAPI, grpcClient *backend.GrpcClient) error {
	link, err := grpcClient.GetLoginLink(getContext(), update.Message.From.ID)
	responseText := link
	if err != nil {
		responseText = err.Error()
	}
	_, err = sendTextMessage(update.Message.Chat.ID, bot, responseText)
	return err
}
