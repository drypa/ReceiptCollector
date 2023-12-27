package commands

import "github.com/go-telegram-bot-api/telegram-bot-api"

type StartCommand struct {
}

func (s StartCommand) Accepted(message string) bool {
	return message == "/start"
}

// Execute command
func (s StartCommand) Execute(update tgbotapi.Update, bot *tgbotapi.BotAPI) error {
	_, err := sendTextMessage(update.Message.Chat.ID, bot, "I'm a bot to collect Your purchase tickets.")
	return err
}
