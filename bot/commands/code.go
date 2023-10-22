package commands

import (
	"github.com/drypa/ReceiptCollector/bot/backend"
	"github.com/drypa/ReceiptCollector/bot/backend/user"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
	"log"
	"regexp"
)

type ConfirmationCodeCommand struct {
	provider   *user.Provider
	grpcClient *backend.GrpcClient
	regexp     *regexp.Regexp
}

func NewConfirmationCodeCommand(provider *user.Provider, grpcClient *backend.GrpcClient) *ConfirmationCodeCommand {
	command := ConfirmationCodeCommand{
		provider:   provider,
		grpcClient: grpcClient,
		regexp:     regexp.MustCompile(`^/code\s+(?P<code>\d{4})$`),
	}
	return &command
}

func (c ConfirmationCodeCommand) Accepted(message string) bool {
	return c.regexp.MatchString(message)
}

func (c ConfirmationCodeCommand) Execute(update tgbotapi.Update, bot *tgbotapi.BotAPI) error {
	userId, err := c.provider.GetUserId(update.Message.From.ID)
	if err != nil {
		return err
	}
	code := c.getCodeFromRequest(update.Message.Text)
	log.Printf("User %s verify phone with code %s\n", userId, code)
	//TODO: send to server
	return nil
}

func (c ConfirmationCodeCommand) getCodeFromRequest(message string) string {
	matches := c.regexp.FindStringSubmatch(message)
	index := c.regexp.SubexpIndex("code")
	code := matches[index]
	return code
}
