package commands

import (
	"github.com/drypa/ReceiptCollector/bot/backend"
	"github.com/drypa/ReceiptCollector/bot/backend/user"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
	"strings"
)

type AddReceiptCommand struct {
	provider   *user.Provider
	grpcClient *backend.GrpcClient
}

func NewAddReceiptCommand(provider *user.Provider, grpcClient *backend.GrpcClient) *AddReceiptCommand {
	return &AddReceiptCommand{provider: provider, grpcClient: grpcClient}
}

func (a AddReceiptCommand) Accepted(message string) bool {
	return strings.Contains(message, "t=") &&
		strings.Contains(message, "s=") &&
		strings.Contains(message, "fn=") &&
		strings.Contains(message, "fp=") &&
		strings.Contains(message, "i=") &&
		strings.Contains(message, "n=")
}

// Execute command
func (a AddReceiptCommand) Execute(update tgbotapi.Update, bot *tgbotapi.BotAPI) error {
	id, err := a.provider.GetUserId(update.Message.From.ID)
	if err != nil {
		return err
	}
	err = tryAddReceipt(id, update.Message.Text, a.grpcClient)

	responseText := "Added"
	if err != nil {
		responseText = err.Error()
	}
	_, err = replyToMessage(update.Message.Chat.ID, bot, responseText, update.Message.MessageID)
	return err
}
