package commands

import (
	"github.com/drypa/ReceiptCollector/bot/backend/user"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
	"regexp"
)

type RegisterCommand struct {
	provider *user.Provider
	regexp   *regexp.Regexp
}

func NewRegisterCommand(provider *user.Provider) *RegisterCommand {
	return &RegisterCommand{
		provider: provider,
		regexp:   regexp.MustCompile(`^/register\s+\+(?P<phone>\d{11})$`),
	}
}

func (r RegisterCommand) Accepted(message string) bool {
	return r.regexp.MatchString(message)
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

func (r RegisterCommand) getPhoneFromRequest(message string) string {
	matches := r.regexp.FindStringSubmatch(message)
	index := r.regexp.SubexpIndex("phone")
	phone := matches[index]
	return phone
}

func register(userId int, client *user.Provider) error {
	_, err := client.GetUserId(userId)
	return err
}
