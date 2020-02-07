package main

import (
	"errors"
	"fmt"
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

func start(options Options) error {

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

	processUpdates(updatesChan, bot)
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

func processUpdates(updatesChan tgbotapi.UpdatesChannel, bot *tgbotapi.BotAPI) {
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
			register(update.Message.From.ID)
			responseText = "You are registered. I collect only your virtual telegram Id."
		default:
			err := tryAddReceipt(update.Message.From.ID, update.Message.Text)
			responseText = "Added"
			if err != nil {
				responseText = err.Error()
			}
		}
		msg := tgbotapi.NewMessage(update.Message.Chat.ID, responseText)
		_, err := bot.Send(msg)
		if err != nil {
			//TODO: do not return error to user
			responseText = err.Error()
		}
	}
}

func tryAddReceipt(userId int, messageText string) error {
	//TODO: validate receipt query string and store
	return nil
}

func register(userId int) {
	//TODO: store user to DB
}
