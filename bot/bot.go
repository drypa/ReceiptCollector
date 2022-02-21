package main

import (
	"context"
	"errors"
	"fmt"
	api "github.com/drypa/ReceiptCollector/api/inside"
	"github.com/drypa/ReceiptCollector/bot/backend"
	"github.com/drypa/ReceiptCollector/bot/backend/report"
	"github.com/drypa/ReceiptCollector/bot/backend/user"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
	"log"
	"net/http"
	"net/url"
	"os"
)

func validateEmpty(value string, emptyErrorMessage string) error {
	if value == "" {
		return errors.New(emptyErrorMessage)
	}
	return nil
}

func getEnvVar(varName string) string {
	value := os.Getenv(varName)
	if varName == "" {
		message, _ := fmt.Scanf("Env variable %s is not set", varName)
		panic(message)
	}
	return value
}

func start(options Options, grpcClient *backend.GrpcClient, reportsClient *report.Client) error {
	provider, err := user.New(grpcClient)
	if err != nil {
		return err
	}
	bot, err := create(options)
	if err != nil {
		log.Printf("Bot create error: %v\n", err)
		return err
	}
	bot.Debug = options.Debug
	log.Printf("Autorised as %s", bot.Self.UserName)

	updateCfg := tgbotapi.NewUpdate(0)
	updatesChan, err := bot.GetUpdatesChan(updateCfg)
	if err != nil {
		log.Println(err)
		return err
	}
	go processNotifications(bot, reportsClient.Notifications)
	processUpdates(updatesChan, bot, grpcClient, provider)

	return nil
}

func processNotifications(bot *tgbotapi.BotAPI, notifications <-chan *api.Report) {
	for {
		select {
		case r := <-notifications:
			msg := tgbotapi.NewMessage(r.TelegramId, r.Message)
			_, err := bot.Send(msg)
			if err != nil {
				log.Printf("Failed to send message '%s' to client %d. %v \n", r.Message, r.TelegramId, err)
			}
		}
	}
}

func create(options Options) (*tgbotapi.BotAPI, error) {
	err := options.validate()
	if err != nil {
		log.Println("Bot options invalid")
		return nil, err
	}
	if options.HttpProxyUrl != "" {
		proxyUrl, err := url.Parse(options.HttpProxyUrl)
		if err != nil {
			log.Println("Proxy url invalid")
			return nil, err
		}

		client := &http.Client{Transport: &http.Transport{Proxy: http.ProxyURL(proxyUrl)}}

		return tgbotapi.NewBotAPIWithClient(options.ApiToken, client)
	}

	return tgbotapi.NewBotAPI(options.ApiToken)
}

func processUpdates(updatesChan tgbotapi.UpdatesChannel,
	bot *tgbotapi.BotAPI,
	grpcClient *backend.GrpcClient,
	provider user.Provider) {
	for update := range updatesChan {
		log.Printf("%v\n", update)
		if update.Message == nil {
			continue
		}
		processMessage(update, bot, provider, grpcClient)
	}
}

func processMessage(update tgbotapi.Update, bot *tgbotapi.BotAPI, provider user.Provider, grpcClient *backend.GrpcClient) {
	var err error
	switch update.Message.Text {
	case "":
		_, err = sendTextMessage(update.Message.Chat.ID, bot, "Please enter a command.")
	case "/start":
		_, err = sendTextMessage(update.Message.Chat.ID, bot, "I'm a bot to collect Your purchase tickets.")
	case "/register":
		err := register(update.Message.From.ID, provider)
		responseText := "You are registered."
		if err != nil {
			responseText = err.Error()
		}
		_, err = sendTextMessage(update.Message.Chat.ID, bot, responseText)
	case "/login":
		link, err := grpcClient.GetLoginLink(context.Background(), update.Message.From.ID)
		responseText := link
		if err != nil {
			responseText = err.Error()
		}
		_, err = sendTextMessage(update.Message.Chat.ID, bot, responseText)
	default:
		_, err = addReceipt(update, bot, provider, grpcClient)
	}
	if err != nil {
		log.Printf("Error while sending response to user %d", update.Message.From.ID)
	}
}

func addReceipt(update tgbotapi.Update, bot *tgbotapi.BotAPI, provider user.Provider, grpcClient *backend.GrpcClient) (tgbotapi.Message, error) {
	id, err := provider.GetUserId(update.Message.From.ID)
	if err == nil {
		err = tryAddReceipt(id, update.Message.Text, grpcClient)
	}
	responseText := "Added"
	if err != nil {
		responseText = err.Error()
	}
	return sendTextMessage(update.Message.Chat.ID, bot, responseText)
}

func sendTextMessage(chatId int64, bot *tgbotapi.BotAPI, responseText string) (tgbotapi.Message, error) {
	msg := tgbotapi.NewMessage(chatId, responseText)
	return bot.Send(msg)
}

func tryAddReceipt(userId string, messageText string, grpc *backend.GrpcClient) error {
	responseMessage, err := grpc.AddReceipt(context.Background(), userId, messageText)
	if err != nil {
		return err
	}
	if responseMessage != "" {
		return errors.New(responseMessage)
	}
	return nil
}

func register(userId int, client user.Provider) error {
	_, err := client.GetUserId(userId)
	return err
}
