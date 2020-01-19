package main

import "log"

func main() {
	options := FromEnv()
	err := Start(options)
	if err != nil {
		log.Fatal(err)
	}
}
