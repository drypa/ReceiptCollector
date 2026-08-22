package main

import "net/http"

// apiHandlers groups all endpoint handlers mounted on the routing tree.
// It allows building the routing layout in isolation (see main_test.go)
// and wiring real controllers in startServer.
type apiHandlers struct {
	marketsBase      http.HandlerFunc // /api/market
	concreteMarket   http.HandlerFunc // GET|PUT|DELETE /api/market/{id}
	getReceipts      http.HandlerFunc // GET /api/receipt
	receiptDetails   http.HandlerFunc // GET /api/receipt/{id}
	deleteReceipt    http.HandlerFunc // DELETE /api/receipt/{id}
	addReceipt       http.HandlerFunc // POST /api/receipt/from-bar-code
	batchAddReceipt  http.HandlerFunc // POST /api/receipt/batch
	addDevice        http.HandlerFunc // POST /api/device
	waste            http.HandlerFunc // GET /api/waste
	login            http.HandlerFunc // POST /api/login, without basic auth
	userRegistration http.HandlerFunc // POST /api/user/register, without basic auth
	internalAccount  http.HandlerFunc // POST /internal/account, without basic auth
	internalReceipt  http.HandlerFunc // POST /internal/receipt, without basic auth
}

// newRootHandler builds the root handler of the http server.
//
// Routing is based on the standard library ServeMux (Go 1.22+ patterns).
// Two trees are used:
//   - authedAPI contains all authenticated /api/* endpoints;
//   - open mounts authedAPI behind requireBasicAuth on "/api/" and registers
//     literal patterns for login/registration/internal endpoints.
//     Literal patterns are more specific than "/api/", so they bypass basic auth.
func newRootHandler(requireBasicAuth func(http.Handler) http.Handler, handlers apiHandlers) http.Handler {
	authedAPI := http.NewServeMux()
	authedAPI.HandleFunc("/api/market", handlers.marketsBase)
	authedAPI.HandleFunc("GET /api/market/{id}", handlers.concreteMarket)
	authedAPI.HandleFunc("PUT /api/market/{id}", handlers.concreteMarket)
	authedAPI.HandleFunc("DELETE /api/market/{id}", handlers.concreteMarket)

	authedAPI.HandleFunc("GET /api/receipt", handlers.getReceipts)
	authedAPI.HandleFunc("GET /api/receipt/{id}", handlers.receiptDetails)
	authedAPI.HandleFunc("DELETE /api/receipt/{id}", handlers.deleteReceipt)
	authedAPI.HandleFunc("POST /api/receipt/from-bar-code", handlers.addReceipt)
	authedAPI.HandleFunc("POST /api/receipt/batch", handlers.batchAddReceipt)

	authedAPI.HandleFunc("POST /api/device", handlers.addDevice)
	authedAPI.HandleFunc("GET /api/waste", handlers.waste)

	open := http.NewServeMux()
	open.Handle("/api/", requireBasicAuth(authedAPI))
	open.HandleFunc("POST /api/login", handlers.login)
	open.HandleFunc("POST /api/user/register", handlers.userRegistration)
	open.HandleFunc("POST /internal/account", handlers.internalAccount)
	open.HandleFunc("POST /internal/receipt", handlers.internalReceipt)

	return open
}
