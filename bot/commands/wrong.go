package commands

import "github.com/go-telegram-bot-api/telegram-bot-api"

type WrongCommand struct {
}

func (w WrongCommand) Accepted(_ string) bool {
	return true
}

// Execute command
func (w WrongCommand) Execute(update tgbotapi.Update, m *Messenger) error {
	_, err := m.SendText("Command not recognized.")
	return err
}
