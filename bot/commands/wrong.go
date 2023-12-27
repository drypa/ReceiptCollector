package commands

import "github.com/go-telegram-bot-api/telegram-bot-api"

type WrongCommand struct {
}

func (w WrongCommand) Accepted(_ string) bool {
	return true
}

// Execute command
func (w WrongCommand) Execute(update tgbotapi.Update, bot *tgbotapi.BotAPI) error {
	_, err := sendTextMessage(update.Message.Chat.ID, bot, "Command not recognized.")
	return err
}
