package commands

import (
	"context"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
	"time"
)

func Empty(update tgbotapi.Update, bot *tgbotapi.BotAPI, err error) error {
	_, err = sendTextMessage(update.Message.Chat.ID, bot, "Please enter a command.")
	return err
}

func sendTextMessage(chatId int64, bot *tgbotapi.BotAPI, responseText string) (tgbotapi.Message, error) {
	msg := tgbotapi.NewMessage(chatId, responseText)
	return bot.Send(msg)
}

func getContext() context.Context {
	ctx, _ := context.WithTimeout(context.Background(), 10*time.Second)
	return ctx
}
