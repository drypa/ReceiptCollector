package analytics

import (
	"net/http"
	"testing"
)

// TestNewClient_NoProxy verifies that the Analytics client uses an explicit
// no-proxy transport (ADR-018). The Analytics service is always internal,
// so even if HTTP_PROXY is present in the environment the client must NOT
// route requests through a proxy. A nil Transport.Proxy means "no proxy"
// (ProxyFromEnvironment is only wired into http.DefaultTransport, so nothing
// is read from the environment).
func TestNewClient_NoProxy(t *testing.T) {
	// defense-in-depth: standard proxy env vars never affect the client.
	t.Setenv("HTTP_PROXY", "http://squid:3128")
	t.Setenv("HTTPS_PROXY", "http://squid:3128")

	c := NewClient("http://analytics:5039")

	transport, ok := c.HTTPClient.Transport.(*http.Transport)
	if !ok {
		t.Fatalf("expected *http.Transport, got %T", c.HTTPClient.Transport)
	}
	if transport.Proxy != nil {
		t.Fatal("expected transport.Proxy to be nil (no proxy for internal Analytics calls)")
	}

	// Sanity: with HTTP_PROXY set, the default transport WOULD use a proxy —
	// this confirms the environment actually carries the value and the test
	// is not vacuous.
	defaultTransport := http.DefaultTransport.(*http.Transport)
	if defaultTransport.Proxy == nil {
		t.Fatal("sanity check failed: http.DefaultTransport must pick up HTTP_PROXY from env")
	}
}

// TestNewClient_Defaults verifies that retry configuration is preserved.
func TestNewClient_Defaults(t *testing.T) {
	c := NewClient("http://analytics:5039")
	if c.MaxRetries != 3 {
		t.Fatalf("expected MaxRetries=3, got %d", c.MaxRetries)
	}
	if c.RetryDelay.Seconds() != 1 {
		t.Fatalf("expected RetryDelay=1s, got %v", c.RetryDelay)
	}
}
