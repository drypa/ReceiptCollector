package commands

import (
	"context"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
	"time"
)

type Command interface {
	Accepted(message string) bool
	Execute(update tgbotapi.Update, bot *tgbotapi.BotAPI) error
}

func sendTextMessage(chatId int64, bot *tgbotapi.BotAPI, responseText string) (tgbotapi.Message, error) {
	msg := tgbotapi.NewMessage(chatId, responseText)
	return bot.Send(msg)
}

func replyToMessage(chatId int64, bot *tgbotapi.BotAPI, responseText string, initialMessageId int) (tgbotapi.Message, error) {
	msg := tgbotapi.NewMessage(chatId, responseText)
	msg.ReplyToMessageID = initialMessageId
	return bot.Send(msg)
}

func getContext() context.Context {
	ctx, _ := context.WithTimeout(context.Background(), 10*time.Second)
	return ctx
}
