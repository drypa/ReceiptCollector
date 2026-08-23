package commands

import (
	"github.com/drypa/ReceiptCollector/bot/analytics"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
)

type GetLoginLinkCommand struct {
	analyticsClient *analytics.Client
}

func NewGetLoginLinkCommand(analyticsClient *analytics.Client) *GetLoginLinkCommand {
	return &GetLoginLinkCommand{analyticsClient: analyticsClient}
}

func (c *GetLoginLinkCommand) Accepted(message string) bool {
	return message == "/login"
}

func (c *GetLoginLinkCommand) Execute(update tgbotapi.Update, bot *tgbotapi.BotAPI) error {
	ctx, cancel := getContext()
	defer cancel()
	link, err := c.analyticsClient.GetLoginLink(ctx, update.Message.From.ID)
	responseText := link
	if err != nil {
		responseText = err.Error()
	}
	_, err = sendTextMessage(update.Message.Chat.ID, bot, responseText)
	return err
}
