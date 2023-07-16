package commands

import (
	"github.com/drypa/ReceiptCollector/bot/backend/user"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
)

type RegisterCommand struct {
	provider *user.Provider
}

func NewRegisterCommand(provider *user.Provider) *RegisterCommand {
	return &RegisterCommand{provider: provider}
}

func (r RegisterCommand) Accepted(message string) bool {
	return message == "/register"
}

func (r RegisterCommand) Execute(update tgbotapi.Update, bot *tgbotapi.BotAPI) error {
	err := register(update.Message.From.ID, r.provider)
	responseText := "You are registered."
	if err != nil {
		responseText = err.Error()
	}
	_, err = sendTextMessage(update.Message.Chat.ID, bot, responseText)
	return err
}

func register(userId int, client *user.Provider) error {
	_, err := client.GetUserId(userId)
	return err
}
