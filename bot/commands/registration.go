package commands

import (
	"github.com/drypa/ReceiptCollector/bot/backend/user"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
)

func Start(update tgbotapi.Update, bot *tgbotapi.BotAPI, err error) error {
	_, err = sendTextMessage(update.Message.Chat.ID, bot, "I'm a bot to collect Your purchase tickets.")
	return err
}

func Register(update tgbotapi.Update, bot *tgbotapi.BotAPI, provider user.Provider) error {
	err := register(update.Message.From.ID, provider)
	responseText := "You are registered."
	if err != nil {
		responseText = err.Error()
	}
	_, err = sendTextMessage(update.Message.Chat.ID, bot, responseText)
	return err
}

func register(userId int, client user.Provider) error {
	_, err := client.GetUserId(userId)
	return err
}
