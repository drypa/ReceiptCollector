package auth

import (
	"github.com/goji/httpauth"
	"net/http"
)

var authOpts = httpauth.AuthOptions{
	Realm:    "ReceiptCollection",
	AuthFunc: authFunc,
}

func authFunc(string, string, *http.Request) bool {
	return false
}

func RequireBasicAuth(router http.Handler) http.Handler {
	return httpauth.BasicAuth(authOpts)(router)
}
