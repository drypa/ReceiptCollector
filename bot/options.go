package main

import "strconv"

type Options struct {
	ApiToken         string
	Debug            bool
	TelegramProxyUrl string
	AnalyticsUrl     string
}

// FromEnv reads bot options from environment variables.
//
// Note: the proxy for the Telegram client is read from TG_PROXY_URL, not from
// the standard HTTP_PROXY/HTTPS_PROXY/NO_PROXY variables (see ADR-018). The
// standard variables would implicitly leak into the whole process via
// http.DefaultTransport and break calls to internal services (Analytics,
// legacy backend). TG_PROXY_URL is not read automatically by Go's
// ProxyFromEnvironment, so the proxy exists only where explicitly declared.
func FromEnv() Options {
	token := getEnvVar("BOT_TOKEN")
	debugString := getEnvVar("BOT_DEBUG")
	proxy := getEnvVar("TG_PROXY_URL")
	analyticsUrl := getEnvVar("ANALYTICS_URL")
	debug := false
	debug, _ = strconv.ParseBool(debugString)

	return Options{
		ApiToken:         token,
		Debug:            debug,
		TelegramProxyUrl: proxy,
		AnalyticsUrl:     analyticsUrl,
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
