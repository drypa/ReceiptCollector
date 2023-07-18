package main

import (
	"errors"
	"fmt"
	api "github.com/drypa/ReceiptCollector/api/inside"
	"github.com/drypa/ReceiptCollector/bot/backend"
	"github.com/drypa/ReceiptCollector/bot/backend/report"
	"github.com/drypa/ReceiptCollector/bot/commands"
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

func start(options Options, grpcClient *backend.GrpcClient, reportsClient *report.Client, registrar *commands.Registrar) error {
	//provider, err := user.New(grpcClient)
	//if err != nil {
	//	return err
	//}
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
	processUpdates(updatesChan, bot, registrar)

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
	registrar *commands.Registrar) {
	for update := range updatesChan {
		if update.Message == nil {
			continue
		}

		processMessage(update, bot, registrar)
	}
}

func processMessage(update tgbotapi.Update, bot *tgbotapi.BotAPI, registrar *commands.Registrar) {
	c := registrar.Get(update.Message.Text)
	if c != nil {
		err := (*c).Execute(update, bot)
		if err != nil {
			log.Printf("Error while process request '%s' from %d. %v", update.Message.Text, update.Message.From.ID, err)
		}

	}

}
