package markets

import (
	"context"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
	"receipt_collector/dispose"
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
	defer dispose.Dispose(func() error { return cursor.Close(ctx) }, "Cursor close error")

	return readMarkets(cursor, ctx)
}

func readMarkets(cursor *mongo.Cursor, context context.Context) ([]Market, error) {
	var markets = make([]Market, 0, 0)
	for cursor.Next(context) {
		var receipt Market
		err := cursor.Decode(&receipt)
		if err != nil {
			return nil, err
		}
		markets = append(markets, receipt)
	}
	return markets, nil
}

func (repository Repository) Insert(ctx context.Context, market Market) error {
	collection := repository.getCollection()

	_, err := collection.InsertOne(ctx, market)
	return err
}

func (repository Repository) Update(ctx context.Context, market Market) error {
	collection := repository.getCollection()

	filter := bson.M{"_id": market.Id}
	_, err := collection.UpdateOne(ctx, filter, market)
	return err
}

func (repository Repository) GetById(ctx context.Context, id string) (Market, error) {
	collection := repository.getCollection()
	market := Market{}
	filter := bson.M{"_id": id}
	result := collection.FindOne(ctx, filter)
	err := result.Err()
	if err != nil {
		return market, err
	}
	err = result.Decode(&market)
	return market, err
}
