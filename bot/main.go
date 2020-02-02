package main

import "log"

func main() {
	options := FromEnv()
	err := start(options)
	if err != nil {
		log.Fatal(err)
	}
}
