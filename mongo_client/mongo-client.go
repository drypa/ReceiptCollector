package mongo_client

import (
	"context"
	"go.mongodb.org/mongo-driver/mongo"
	"go.mongodb.org/mongo-driver/mongo/options"
	"time"
)

func GetMongoClient(mongoUrl string, mongoUser string, mongoSecret string) *mongo.Client {
	client, err := mongo.NewClient(options.Client().ApplyURI(mongoUrl).SetAuth(options.Credential{Username: mongoUser, Password: mongoSecret}))
	check(err)
	ctx, _ := context.WithTimeout(context.Background(), 10*time.Second)
	err = client.Connect(ctx)
	check(err)
	return client
}

func check(err error) {
	if err != nil {
		panic(err)
	}
}
