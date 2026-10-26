package commands

import (
	"context"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
	"time"
)

type Command interface {
	Accepted(message string) bool
	Execute(update tgbotapi.Update, m *Messenger) error
}

func getContext() (context.Context, context.CancelFunc) {
	return context.WithTimeout(context.Background(), 10*time.Second)
}
