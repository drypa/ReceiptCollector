package main

import (
	"context"
	"errors"
	"fmt"
	"github.com/drypa/ReceiptCollector/bot/backend"
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

func start(options Options, client backend.Client, grpcClient *backend.GrpcClient) error {
	provider, err := user.New(client, grpcClient)
	if err != nil {
		return err
	}
	bot, err := create(options)
	if err != nil {
		log.Println("Bot create error")
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

	processUpdates(updatesChan, bot, client, grpcClient, provider)
	return nil
}

func create(options Options) (*tgbotapi.BotAPI, error) {
	err := options.validate()
	if err != nil {
		log.Println("Bot options invalid")
		return nil, err
	}
	if options.HttpProxyUrl != "" {
		url, err := url.Parse(options.HttpProxyUrl)
		if err != nil {
			log.Println("Proxy url invalid")
			return nil, err
		}

		client := &http.Client{Transport: &http.Transport{Proxy: http.ProxyURL(url)}}

		return tgbotapi.NewBotAPIWithClient(options.ApiToken, client)
	}

	return tgbotapi.NewBotAPI(options.ApiToken)
}

func processUpdates(updatesChan tgbotapi.UpdatesChannel,
	bot *tgbotapi.BotAPI,
	client backend.Client,
	grpcClient *backend.GrpcClient,
	provider user.Provider) {
	for update := range updatesChan {
		log.Printf("%v\n", update)
		if update.Message == nil {
			continue
		}
		var responseText string
		switch update.Message.Text {
		case "":
			responseText = "Please enter a command."
		case "/start":
			responseText = "I'm a bot to collect Your purchase tickets."
		case "/register":
			err := register(update.Message.From.ID, provider)
			if err != nil {
				responseText = err.Error()
			} else {
				responseText = "You are registered."
			}
		case "/login":
			link, err := grpcClient.GetLoginLink(context.Background(), update.Message.From.ID)
			if err != nil {
				responseText = err.Error()
			} else {
				responseText = link
			}
		default:
			id, err := provider.GetUserId(update.Message.From.ID)
			if err == nil {
				err = tryAddReceipt(id, update.Message.Text, grpcClient)
			}
			if err != nil {
				responseText = err.Error()
			} else {
				responseText = "Added"
			}
		}
		msg := tgbotapi.NewMessage(update.Message.Chat.ID, responseText)
		_, err := bot.Send(msg)
		if err != nil {
			log.Printf("Error while sending response to user %d", update.Message.From.ID)
		}
	}
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
