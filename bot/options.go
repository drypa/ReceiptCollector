package main

import "strconv"

type Options struct {
	ApiToken string
	Debug    bool
}

func FromEnv() Options {
	token := getEnvVar("BOT_TOKEN")
	debugString := getEnvVar("BOT_DEBUG")
	debug := false
	debug, _ = strconv.ParseBool(debugString)

	return Options{
		ApiToken: token,
		Debug:    debug,
	}
}

func (options Options) validate() error {
	err := validateEmpty(options.ApiToken, "Api token is not set")
	if err != nil {
		return err
	}

	return nil
}
