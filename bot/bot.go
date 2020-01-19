package main

import (
	"errors"
	"fmt"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
	"log"
	"net/http"
	"os"
	"strconv"
)

type Options struct {
	ApiToken   string
	Debug      bool
	WebHookUrl string
	CertPath   string
	KeyPath    string
}

func FromEnv() Options {
	token := getEnvVar("BOT_TOKEN")
	webHookUrl := getEnvVar("BOT_WEB_HOOK_URL")
	certPath := getEnvVar("BOT_CERT_PATH")
	keyPath := getEnvVar("BOT_KEY_PATH")
	debugString := getEnvVar("BOT_DEBUG")
	debug := false
	debug, _ = strconv.ParseBool(debugString)

	return Options{
		ApiToken:   token,
		Debug:      debug,
		WebHookUrl: webHookUrl,
		CertPath:   certPath,
		KeyPath:    keyPath,
	}
}

func (options Options) validate() error {
	err := validateEmpty(options.ApiToken, "Api token is not set")
	if err != nil {
		return err
	}
	err = validateEmpty(options.WebHookUrl, "Web hook url is not set")
	if err != nil {
		return err
	}
	err = validateEmpty(options.CertPath, "Certificate path is not set")
	if err != nil {
		return err
	}
	err = validateEmpty(options.KeyPath, "SSL key file path is not set")
	if err != nil {
		return err
	}
	return nil
}

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
func Start(options Options) error {
	//err := options.validate()
	//if err != nil {
	//	log.Println("Bot options invalid")
	//	return err
	//}

	bot, err := tgbotapi.NewBotAPI(options.ApiToken)
	if err != nil {
		log.Println("Bot create error")
		return err
	}
	bot.Debug = options.Debug

	log.Printf("Autorised as %s", bot.Self.UserName)
	//config := tgbotapi.NewWebhookWithCert(options.WebHookUrl+bot.Token, options.CertPath)
	//_, err = bot.SetWebhook(config)
	//if err != nil {
	//	log.Println("Web hook create error")
	//	return err
	//}
	//info, err := bot.GetWebhookInfo()
	//if err != nil {
	//	log.Println("Web hook error")
	//	return err
	//}
	//if info.LastErrorDate != 0 {
	//	log.Printf("Telegram callback failed. %s\n", info.LastErrorMessage)
	//}
	//updatesChan := bot.ListenForWebhook("/" + bot.Token)
	updateCfg := tgbotapi.NewUpdate(0)
	updatesChan, err := bot.GetUpdatesChan(updateCfg)
	if err != nil {
		log.Println(err)
		return err
	}

	go http.ListenAndServeTLS(":8443", options.CertPath, options.KeyPath, nil)

	processUpdates(updatesChan, bot)
	return nil
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
		bot.Send(msg)
	}
}

func tryAddReceipt(userId int, messageText string) error {
	//TODO: validate receipt query string and store
	panic("not implemented exception")
}

func register(userId int) {
	//TODO: store user to DB
}
