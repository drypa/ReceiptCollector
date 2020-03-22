package users

import (
	"context"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/bson/primitive"
	"go.mongodb.org/mongo-driver/mongo"
	"time"
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
func (repository Repository) Insert(ctx context.Context, user *User) error {
	collection := repository.getCollection()

	document, err := collection.InsertOne(ctx, user)
	if err != nil {
		return err
	}
	user.Id = document.InsertedID.(primitive.ObjectID)
	return nil
}

//GetByLogin returns User by login.
func (repository Repository) GetByLogin(ctx context.Context, login string) (User, error) {
	collection := repository.getCollection()

	var user User
	err := collection.FindOne(ctx, bson.D{{"name", login}}).Decode(&user)

	return user, err
}

//GetByTelegramId returns User by telegram id.
func (repository Repository) GetByTelegramId(ctx context.Context, telegramId int) (*User, error) {
	collection := repository.getCollection()

	var user User
	err := collection.FindOne(ctx, bson.D{{"telegram_id", telegramId}}).Decode(&user)
	if err == mongo.ErrNoDocuments {
		return nil, nil
	}
	return &user, err
}

//GetAll returns all users.
func (repository Repository) GetAll(ctx context.Context) ([]User, error) {
	collection := repository.getCollection()
	cursor, err := collection.Find(ctx, bson.D{})
	if err != nil {
		return nil, err
	}
	return readUsers(cursor, ctx)
}

//UpdateLoginLink set one-time unique link to login user.
func (repository Repository) UpdateLoginLink(ctx context.Context, userId primitive.ObjectID, url string, expiration time.Time) {
	//TODO: need implement
}

func readUsers(cursor *mongo.Cursor, context context.Context) ([]User, error) {
	var receipts = make([]User, 0, 0)
	for cursor.Next(context) {
		var user User
		err := cursor.Decode(&user)
		if err != nil {
			return nil, err
		}
		receipts = append(receipts, user)
	}
	return receipts, nil
}
