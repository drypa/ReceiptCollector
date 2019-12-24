package markets

import (
	"context"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
	"receipt_collector/utils"
)

type Repository struct {
	client *mongo.Client
}

func NewRepository(client *mongo.Client) Repository {
	repository := Repository{client: client}
	return repository
}

func (repository Repository) getCollection() *mongo.Collection {
	return repository.client.Database("receipt_collection").Collection("markets")
}

func (repository Repository) GetAll(ctx context.Context) ([]Market, error) {
	collection := repository.getCollection()
	cursor, err := collection.Find(ctx, bson.D{})
	if err != nil {
		return nil, err
	}
	defer utils.Dispose(func() error { return cursor.Close(ctx) }, "Cursor close error")

	return readMarkets(cursor, ctx)
}

func readMarkets(cursor *mongo.Cursor, context context.Context) ([]Market, error) {
	var receipts = make([]Market, 0, 0)
	for cursor.Next(context) {
		var receipt Market
		err := cursor.Decode(&receipt)
		if err != nil {
			return nil, err
		}
		receipts = append(receipts, receipt)
	}
	return receipts, nil
}
