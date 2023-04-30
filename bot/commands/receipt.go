package commands

import (
	"errors"
	"fmt"
	"github.com/drypa/ReceiptCollector/bot/backend"
	"github.com/drypa/ReceiptCollector/bot/backend/user"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
	"log"
)

// AddReceipt executes add new receipt by QR code command.
func AddReceipt(update tgbotapi.Update, bot *tgbotapi.BotAPI, provider user.Provider, grpcClient *backend.GrpcClient) error {
	id, err := provider.GetUserId(update.Message.From.ID)
	if err != nil {
		return err
	}
	err = tryAddReceipt(id, update.Message.Text, grpcClient)

	responseText := "Added"
	if err != nil {
		responseText = err.Error()
	}
	_, err = replyToMessage(update.Message.Chat.ID, bot, responseText, update.Message.MessageID)
	return err
}

// GetReceiptReport is used to get receipt file by QR code.
func GetReceiptReport(update tgbotapi.Update, bot *tgbotapi.BotAPI, provider user.Provider, grpcClient *backend.GrpcClient) error {
	id, err := provider.GetUserId(update.Message.From.ID)
	if err != nil {
		return err
	}
	qr, err := getQr(update)
	if err != nil {
		return err
	}

	report, fileName, err := grpcClient.GetReceiptReport(getContext(), id, qr)
	if err != nil {
		return err
	}
	log.Println("report is", len(report))
	file := tgbotapi.FileBytes{
		Name:  fileName,
		Bytes: nil,
	}
	tgbotapi.NewDocumentUpload(update.Message.Chat.ID, file)
	//TODO: return report file to chat
	outMessage := fmt.Sprintf("got %d bytes file %s", len(file.Bytes), file.Name)
	_, err = replyToMessage(update.Message.Chat.ID, bot, outMessage, update.Message.MessageID)
	return err
}

func getQr(update tgbotapi.Update) (string, error) {
	if update.Message.ReplyToMessage != nil {
		return update.Message.ReplyToMessage.Text, nil
	}
	return "", errors.New("need reply for QR request message")

}

func tryAddReceipt(userId string, messageText string, grpc *backend.GrpcClient) error {
	responseMessage, err := grpc.AddReceipt(getContext(), userId, messageText)
	if err != nil {
		return err
	}
	if responseMessage != "" {
		return errors.New(responseMessage)
	}
	return nil
}
