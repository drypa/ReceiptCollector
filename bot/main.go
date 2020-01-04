package main

import "log"
import bot "github.com/drypa/receipt-telegram-bot"

func main() {
	options := bot.Options{}
	err := bot.Start(options)
	if err != nil {
		log.Fatal(err)
	}
}
