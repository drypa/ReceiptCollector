package main

import (
	"encoding/base64"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/goji/httpauth"
	"receipt_collector/markets"
	"receipt_collector/receipts"
)

const (
	stubLogin    = "user"
	stubPassword = "pass"
	realm        = "ReceiptCollection"
)

func okHandler(writer http.ResponseWriter, request *http.Request) {
	writer.WriteHeader(http.StatusOK)
}

func stubHandlers() apiHandlers {
	return apiHandlers{
		marketsBase:      okHandler,
		concreteMarket:   okHandler,
		getReceipts:      okHandler,
		receiptDetails:   okHandler,
		deleteReceipt:    okHandler,
		addReceipt:       okHandler,
		batchAddReceipt:  okHandler,
		addDevice:        okHandler,
		waste:            okHandler,
		login:            okHandler,
		userRegistration: okHandler,
		internalAccount:  okHandler,
		internalReceipt:  okHandler,
	}
}

// stubBasicAuth mimics auth.BasicAuth wiring (goji/httpauth) without MongoDB:
// only user/pass pair is accepted.
func stubBasicAuth() func(http.Handler) http.Handler {
	options := httpauth.AuthOptions{
		Realm: realm,
		AuthFunc: func(login string, password string, request *http.Request) bool {
			return login == stubLogin && password == stubPassword
		},
	}
	return httpauth.BasicAuth(options)
}

// newTestRoot builds root handler with stub handlers.
func newTestRoot() http.Handler {
	return newRootHandler(stubBasicAuth(), stubHandlers())
}

// newTestRootWithRealIdValidation builds root handler where endpoints
// extracting {id} use real controllers. Handlers are invoked only for
// invalid ids (rejected before repository access), so zero-value
// repositories (nil mongo client) are safe here.
func newTestRootWithRealIdValidation() http.Handler {
	handlers := stubHandlers()
	handlers.receiptDetails = receipts.New(receipts.Repository{}).GetReceiptDetailsHandler
	handlers.deleteReceipt = receipts.New(receipts.Repository{}).DeleteReceiptHandler
	handlers.concreteMarket = markets.New(markets.Repository{}).ConcreteMarketHandler
	return newRootHandler(stubBasicAuth(), handlers)
}

func basicAuthHeader(login string, password string) string {
	return "Basic " + base64.StdEncoding.EncodeToString([]byte(login+":"+password))
}

type routeCase struct {
	name          string
	method        string
	path          string
	authorization string
	expected      int
}

func runRouteCases(t *testing.T, handler http.Handler, cases []routeCase) {
	t.Helper()
	for _, test := range cases {
		t.Run(test.name, func(t *testing.T) {
			request := httptest.NewRequest(test.method, test.path, nil)
			if test.authorization != "" {
				request.Header.Set("Authorization", test.authorization)
			}
			recorder := httptest.NewRecorder()
			handler.ServeHTTP(recorder, request)
			if recorder.Code != test.expected {
				t.Errorf("%s %s with Authorization=%q: got status %d, want %d",
					test.method, test.path, test.authorization, recorder.Code, test.expected)
			}
		})
	}
}

// TestRouteMatrix verifies the routing table from ADR-014 p.1:
// same paths and methods, 405 on unsupported method, 404 on unknown path,
// BasicAuth applied to /api/* and not to /internal/* and login/register.
func TestRouteMatrix(t *testing.T) {
	valid := basicAuthHeader(stubLogin, stubPassword)
	invalid := basicAuthHeader(stubLogin, "wrong-password")
	cases := []routeCase{
		// Registered routes behave as before the migration.
		{name: "GET /api/receipt", method: http.MethodGet, path: "/api/receipt", authorization: valid, expected: http.StatusOK},
		{name: "GET /api/receipt/{id}", method: http.MethodGet, path: "/api/receipt/5dc1c9427126cc2841ca384d", authorization: valid, expected: http.StatusOK},
		{name: "DELETE /api/receipt/{id}", method: http.MethodDelete, path: "/api/receipt/5dc1c9427126cc2841ca384d", authorization: valid, expected: http.StatusOK},
		{name: "POST /api/receipt/from-bar-code", method: http.MethodPost, path: "/api/receipt/from-bar-code", authorization: valid, expected: http.StatusOK},
		{name: "POST /api/receipt/batch", method: http.MethodPost, path: "/api/receipt/batch", authorization: valid, expected: http.StatusOK},
		{name: "GET /api/market", method: http.MethodGet, path: "/api/market", authorization: valid, expected: http.StatusOK},
		{name: "POST /api/market", method: http.MethodPost, path: "/api/market", authorization: valid, expected: http.StatusOK},
		{name: "PUT /api/market/{id}", method: http.MethodPut, path: "/api/market/abc123", authorization: valid, expected: http.StatusOK},
		{name: "GET /api/market/{id}", method: http.MethodGet, path: "/api/market/abc123", authorization: valid, expected: http.StatusOK},
		{name: "DELETE /api/market/{id}", method: http.MethodDelete, path: "/api/market/abc123", authorization: valid, expected: http.StatusOK},
		{name: "POST /api/device", method: http.MethodPost, path: "/api/device", authorization: valid, expected: http.StatusOK},
		{name: "GET /api/waste", method: http.MethodGet, path: "/api/waste", authorization: valid, expected: http.StatusOK},

		// Routes without BasicAuth.
		{name: "POST /api/login without credentials", method: http.MethodPost, path: "/api/login", authorization: "", expected: http.StatusOK},
		{name: "POST /api/user/register without credentials", method: http.MethodPost, path: "/api/user/register", authorization: "", expected: http.StatusOK},
		{name: "POST /internal/account without credentials", method: http.MethodPost, path: "/internal/account", authorization: "", expected: http.StatusOK},
		{name: "POST /internal/receipt without credentials", method: http.MethodPost, path: "/internal/receipt", authorization: "", expected: http.StatusOK},

		// Unsupported methods are rejected with 405 as gorilla/mux did.
		{name: "POST /api/receipt not allowed", method: http.MethodPost, path: "/api/receipt", authorization: valid, expected: http.StatusMethodNotAllowed},
		{name: "PATCH /api/receipt not allowed", method: http.MethodPatch, path: "/api/receipt", authorization: valid, expected: http.StatusMethodNotAllowed},
		{name: "POST /api/waste not allowed", method: http.MethodPost, path: "/api/waste", authorization: valid, expected: http.StatusMethodNotAllowed},
		{name: "PUT /api/device not allowed", method: http.MethodPut, path: "/api/device", authorization: valid, expected: http.StatusMethodNotAllowed},
		// Unknown paths yield 404.
		{name: "unknown path outside /api", method: http.MethodGet, path: "/unknown", authorization: "", expected: http.StatusNotFound},
		{name: "unknown path inside /api", method: http.MethodGet, path: "/api/unknown", authorization: valid, expected: http.StatusNotFound},

		// BasicAuth is enforced on /api/* (except login/register).
		{name: "GET /api/receipt without credentials is unauthorized", method: http.MethodGet, path: "/api/receipt", authorization: "", expected: http.StatusUnauthorized},
		{name: "GET /api/receipt with wrong password is unauthorized", method: http.MethodGet, path: "/api/receipt", authorization: invalid, expected: http.StatusUnauthorized},
		{name: "POST /api/device without credentials is unauthorized", method: http.MethodPost, path: "/api/device", authorization: "", expected: http.StatusUnauthorized},
	}
	runRouteCases(t, newTestRoot(), cases)
}

// TestInvalidPathParameterRejectedWithNotFound checks that ids rejected by
// route.PathID produce 404 — same as unmatched {id:[a-zA-Z0-9]+} in gorilla/mux.
func TestInvalidPathParameterRejectedWithNotFound(t *testing.T) {
	valid := basicAuthHeader(stubLogin, stubPassword)
	cases := []routeCase{
		{name: "invalid receipt id on GET", method: http.MethodGet, path: "/api/receipt/a.b", authorization: valid, expected: http.StatusNotFound},
		{name: "invalid receipt id on DELETE", method: http.MethodDelete, path: "/api/receipt/a.b", authorization: valid, expected: http.StatusNotFound},
		{name: "invalid market id on GET", method: http.MethodGet, path: "/api/market/a.b", authorization: valid, expected: http.StatusNotFound},
		{name: "invalid market id on PUT", method: http.MethodPut, path: "/api/market/a.b", authorization: valid, expected: http.StatusNotFound},
		// {id} wildcard also matches literal route segments like from-bar-code,
		// which gorilla regex rejected; validation restores the 404.
		{name: "DELETE on /api/receipt/from-bar-code", method: http.MethodDelete, path: "/api/receipt/from-bar-code", authorization: valid, expected: http.StatusNotFound},
	}
	runRouteCases(t, newTestRootWithRealIdValidation(), cases)
}
