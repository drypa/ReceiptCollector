package waste

import (
	"context"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
	"time"
)

type Repository struct {
	client *mongo.Client
}

func NewRepository(client *mongo.Client) Repository {
	repository := Repository{client: client}
	return repository
}

//QueryFilter specifies query parameters to get wastes.
type QueryFilter struct {
	UserId string
	From   *time.Time
	To     *time.Time
}

func (repository Repository) GetForUser(ctx context.Context, ownerId string) ([]Waste, error) {
	collection := repository.getCollection()
	cursor, err := collection.Find(ctx, bson.D{{"owner_id", ownerId}})
	if err != nil {
		return nil, err
	}
	wastes, err := readWastes(ctx, cursor)
	if err != nil {
		return nil, err
	}
	return wastes, nil
}

func readWastes(ctx context.Context, cursor *mongo.Cursor) ([]Waste, error) {
	wastes := make([]Waste, 0, 0)
	waste := Waste{}
	for cursor.Next(ctx) {
		err := cursor.Decode(&waste)
		if err != nil {
			return nil, err
		}
		wastes = append(wastes, waste)
	}
	return wastes, nil
}

func (repository Repository) GetByFilter(ctx context.Context, filter QueryFilter) ([]Waste, error) {
	collection := repository.getCollection()
	query := bson.D{{"owner_id", filter.UserId}}

	if filter.From != nil {
		query = append(query, bson.E{Key: "date", Value: bson.E{Key: "$gte", Value: filter.From}})
	}
	if filter.To != nil {
		query = append(query, bson.E{Key: "date", Value: bson.E{Key: "$lte", Value: filter.To}})
	}
	cursor, err := collection.Find(ctx, query)
	if err != nil {
		return nil, err
	}
	wastes, err := readWastes(ctx, cursor)
	return wastes, err
}

func (repository Repository) Add(ctx context.Context, waste Waste) error {
	collection := repository.getCollection()
	_, err := collection.InsertOne(ctx, waste)
	return err
}

func (repository Repository) getCollection() *mongo.Collection {
	return repository.client.Database("receipt_collection").Collection("wastes")
}
