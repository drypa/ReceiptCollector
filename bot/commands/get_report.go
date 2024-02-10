package commands

import (
	"github.com/drypa/ReceiptCollector/bot/backend"
	"github.com/drypa/ReceiptCollector/bot/backend/user"
	"github.com/go-telegram-bot-api/telegram-bot-api"
	"log"
)

type GetReceiptReportCommand struct {
	provider   *user.Provider
	grpcClient *backend.GrpcClient
}

func NewGetReceiptReportCommand(provider *user.Provider, grpcClient *backend.GrpcClient) *GetReceiptReportCommand {
	return &GetReceiptReportCommand{provider: provider, grpcClient: grpcClient}
}

func (g GetReceiptReportCommand) Accepted(message string) bool {
	return message == "/get"
}

// Execute command
func (g GetReceiptReportCommand) Execute(update tgbotapi.Update, bot *tgbotapi.BotAPI) error {
	log.Printf("GetReceiptReportCommand: %s", update.Message.Text)
	id, err := g.provider.GetUserId(update.Message.From.ID)
	if err != nil {
		return err
	}
	qr, err := getQr(update)
	if err != nil {
		return err
	}

	report, fileName, err := g.grpcClient.GetReceiptReport(getContext(), id, qr)
	if err != nil {
		return err
	}
	file := tgbotapi.FileBytes{
		Name:  fileName,
		Bytes: report,
	}
	upload := tgbotapi.NewDocumentUpload(update.Message.Chat.ID, file)
	upload.ReplyToMessageID = update.Message.MessageID
	_, err = bot.Send(upload)

	return err
}
