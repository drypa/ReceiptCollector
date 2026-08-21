package main

import "strconv"

type Options struct {
	ApiToken     string
	Debug        bool
	HttpProxyUrl string
	AnalyticsUrl string
}

func FromEnv() Options {
	token := getEnvVar("BOT_TOKEN")
	debugString := getEnvVar("BOT_DEBUG")
	proxy := getEnvVar("HTTP_PROXY")
	analyticsUrl := getEnvVar("ANALYTICS_URL")
	debug := false
	debug, _ = strconv.ParseBool(debugString)

	return Options{
		ApiToken:     token,
		Debug:        debug,
		HttpProxyUrl: proxy,
		AnalyticsUrl: analyticsUrl,
	}
}

func (options Options) validate() error {
	err := validateEmpty(options.ApiToken, "Api token is not set")
	if err != nil {
		return err
	}

	err = validateEmpty(options.AnalyticsUrl, "Analytics URL is not set")
	if err != nil {
		return err
	}

	return nil
}
