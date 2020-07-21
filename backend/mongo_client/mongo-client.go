package mongo_client

import (
	"context"
	"go.mongodb.org/mongo-driver/mongo"
	"go.mongodb.org/mongo-driver/mongo/options"
	"time"
)

//Settings - mongo connection settings.
type Settings struct {
	url    string
	user   string
	secret string
}

//NewSettings creates Settings.
func NewSettings(url string, user string, secret string) Settings {
	return Settings{
		url:    url,
		user:   user,
		secret: secret,
	}
}

//New creates initialized mongo client.
func New(settings Settings) (*mongo.Client, error) {
	opts := options.Client().ApplyURI(settings.url).SetAuth(options.Credential{Username: settings.user, Password: settings.secret})
	client, err := mongo.NewClient(opts)
	if err != nil {
		return nil, err
	}

	ctx, _ := context.WithTimeout(context.Background(), 10*time.Second)
	err = client.Connect(ctx)
	if err != nil {
		return nil, err
	}
	return client, nil
}
