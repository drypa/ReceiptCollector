package main

import "testing"

// TestFromEnv_TGProxyUrl verifies that the proxy for the Telegram client is
// read from TG_PROXY_URL and NOT from the standard HTTP_PROXY variable
// (ADR-018: standard proxy env variables must not leak into the process).
func TestFromEnv_TGProxyUrl(t *testing.T) {
	t.Setenv("BOT_TOKEN", "test-token")
	t.Setenv("BOT_DEBUG", "true")
	t.Setenv("ANALYTICS_URL", "http://localhost:5039")

	t.Run("reads TG_PROXY_URL", func(t *testing.T) {
		t.Setenv("TG_PROXY_URL", "http://squid:3128")
		// HTTP_PROXY must be ignored even if set.
		t.Setenv("HTTP_PROXY", "http://ignored-proxy:3128")

		options := FromEnv()
		if options.TelegramProxyUrl != "http://squid:3128" {
			t.Fatalf("expected TelegramProxyUrl 'http://squid:3128', got '%s'", options.TelegramProxyUrl)
		}
	})

	t.Run("empty TG_PROXY_URL means no proxy", func(t *testing.T) {
		t.Setenv("TG_PROXY_URL", "")
		t.Setenv("HTTP_PROXY", "http://would-be-ignored:3128")

		options := FromEnv()
		if options.TelegramProxyUrl != "" {
			t.Fatalf("expected empty TelegramProxyUrl, got '%s'", options.TelegramProxyUrl)
		}
	})
}
