package users

import (
	"context"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
)

//Repository provides methods to persist users.
type Repository struct {
	client *mongo.Client
}

//NewRepository constructs Repository.
func NewRepository(client *mongo.Client) Repository {
	repository := Repository{client: client}
	return repository
}

func (repository Repository) getCollection() *mongo.Collection {
	return repository.client.Database("receipt_collection").Collection("system_users")
}

//Insert new User.
func (repository Repository) Insert(ctx context.Context, user User) error {
	collection := repository.getCollection()

	_, err := collection.InsertOne(ctx, user)
	return err
}

//GetByLogin returns User by login.
func (repository Repository) GetByLogin(ctx context.Context, login string) (User, error) {
	collection := repository.getCollection()

	var user User
	err := collection.FindOne(ctx, bson.D{{"name", login}}).Decode(&user)

	return user, err
}

//GetByTelegramId returns User by telegram id.
func (repository Repository) GetByTelegramId(ctx context.Context, telegramId string) (*User, error) {
	collection := repository.getCollection()

	var user User
	err := collection.FindOne(ctx, bson.D{{"telegram_id", telegramId}}).Decode(&user)
	if err == mongo.ErrNoDocuments {
		return nil, nil
	}
	return &user, err
}
