package waste

import (
	"context"
	"errors"
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

func (repository Repository) AddByReceipt(query Query, receiptId string) error {
	//TODO: implement this!
	return errors.New("not implemented")
}

func (repository Repository) GetForUser(ctx context.Context, ownerId string) ([]Waste, error) {
	collection := repository.getCollection()
	wastes := make([]Waste, 0, 0)
	cursor, err := collection.Find(ctx, bson.D{{"owner_id": ownerId}})
	if err != nil {
		return nil, err
	}
	waste := Waste{}
	for cursor.Next(ctx) {
		err := cursor.Decode(waste)
		if err != nil {
			return nil, err
		}
		wastes = append(wastes, waste)
	}
	return wastes, nil
}

func (repository Repository) Add(ctx context.Context, waste Waste) error {
	collection := repository.getCollection()
	_, err := collection.InsertOne(ctx, waste)
	return err
}

//TODO: add manually(name, sum, description, category)

func (repository Repository) getCollection() *mongo.Collection {
	return repository.client.Database("receipt_collection").Collection("wastes")
}
