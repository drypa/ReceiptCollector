package users

import (
	"context"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
)

type Repository struct {
	client *mongo.Client
}

func NewRepository(client *mongo.Client) Repository {
	repository := Repository{client: client}
	return repository
}

func (repository Repository) getCollection() *mongo.Collection {
	return repository.client.Database("receipt_collection").Collection("system_users")
}

func (repository Repository) Insert(ctx context.Context, user User) error {
	collection := repository.getCollection()

	_, err := collection.InsertOne(ctx, user)
	return err
}

func (repository Repository) GetByLogin(ctx context.Context, login string) (User, error) {
	collection := repository.getCollection()

	var user User
	err := collection.FindOne(ctx, bson.D{{"name", login}}).Decode(&user)

	return user, err
}
