package backend

import (
	"net/http"
	"testing"
)

// TestNew_NoProxy verifies that the legacy backend client uses an explicit
// no-proxy transport (ADR-018). This protects local dev runs where HTTP_PROXY
// may be set in the developer's shell. A nil Transport.Proxy means "no proxy"
// (ProxyFromEnvironment is only wired into http.DefaultTransport, so nothing
// is read from the environment).
func TestNew_NoProxy(t *testing.T) {
	// defense-in-depth: standard proxy env vars never affect the client.
	t.Setenv("HTTP_PROXY", "http://squid:3128")
	t.Setenv("HTTPS_PROXY", "http://squid:3128")

	client := New("http://localhost:8888")

	if client.httpClient == nil {
		t.Fatal("expected httpClient to be initialized")
	}
	transport, ok := client.httpClient.Transport.(*http.Transport)
	if !ok {
		t.Fatalf("expected *http.Transport, got %T", client.httpClient.Transport)
	}
	if transport.Proxy != nil {
		t.Fatal("expected transport.Proxy to be nil (no proxy for internal backend calls)")
	}
}
