package commands

import (
	"context"
	"github.com/drypa/ReceiptCollector/bot/backend"
	"github.com/drypa/ReceiptCollector/bot/backend/user"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
	"regexp"
)

type RegisterCommand struct {
	provider   *user.Provider
	regexp     *regexp.Regexp
	grpcClient *backend.GrpcClient
}

func NewRegisterCommand(provider *user.Provider, grpcClient *backend.GrpcClient) *RegisterCommand {
	return &RegisterCommand{
		provider:   provider,
		grpcClient: grpcClient,
		regexp:     regexp.MustCompile(`^/register\s+(?P<phone>\+\d{11})$`),
	}
}

func (r RegisterCommand) Accepted(message string) bool {
	return r.regexp.MatchString(message)
}

func (r RegisterCommand) Execute(update tgbotapi.Update, bot *tgbotapi.BotAPI) error {
	phone := r.getPhoneFromRequest(update.Message.Text)
	err := r.register(update.Message.From.ID, phone, r.provider)
	responseText := "You are registered."
	if err != nil {
		responseText = err.Error()
	}
	_, err = sendTextMessage(update.Message.Chat.ID, bot, responseText)
	return err
}

func (r RegisterCommand) getPhoneFromRequest(message string) string {
	matches := r.regexp.FindStringSubmatch(message)
	index := r.regexp.SubexpIndex("phone")
	phone := matches[index]
	return phone
}

func (r RegisterCommand) register(telegramId int, phone string, client *user.Provider) error {
	err := r.grpcClient.RegisterUser(context.Background(), telegramId, phone)
	return err
}
