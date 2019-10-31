package mongo_client

import (
	"context"
	"go.mongodb.org/mongo-driver/mongo"
	"go.mongodb.org/mongo-driver/mongo/options"
	"time"
)

func GetMongoClient(mongoUrl string, mongoUser string, mongoSecret string) (*mongo.Client, error) {
	client, err := mongo.NewClient(options.Client().ApplyURI(mongoUrl).SetAuth(options.Credential{Username: mongoUser, Password: mongoSecret}))
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
