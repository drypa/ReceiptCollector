package auth

import (
	"context"
	"github.com/goji/httpauth"
	"net/http"
	"receipt_collector/passwords"
	"receipt_collector/users"
)

type ContextKey string

const (
	UserId = ContextKey("userId")
)

type BasicAuth struct {
	repository users.Repository
}

func New(repository users.Repository) BasicAuth {
	return BasicAuth{
		repository: repository,
	}
}

func (basicAuth BasicAuth) authFunc(login string, password string, request *http.Request) bool {
	ctx := request.Context()

	user, err := basicAuth.repository.GetByLogin(ctx, login)
	if err != nil {
		return false
	}
	isPasswordValid := passwords.ComparePasswordWithHash(user.PasswordHash, password)
	if isPasswordValid {
		newContext := context.WithValue(ctx, UserId, user.Id.Hex())
		*request = *request.WithContext(newContext)
	}
	return isPasswordValid
}

func (basicAuth BasicAuth) RequireBasicAuth(router http.Handler) http.Handler {
	options := httpauth.AuthOptions{
		Realm:    "ReceiptCollection",
		AuthFunc: basicAuth.authFunc,
	}
	return httpauth.BasicAuth(options)(router)
}
