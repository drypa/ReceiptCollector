package commands

import "github.com/go-telegram-bot-api/telegram-bot-api"

type EmptyCommand struct {
}

func (e EmptyCommand) Accepted(message string) bool {
	return message == ""
}

// Execute command
func (e EmptyCommand) Execute(update tgbotapi.Update, m *Messenger) error {
	_, err := m.SendText("Please enter a command.")
	return err
}
