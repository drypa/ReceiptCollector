package dal

import (
	"context"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/bson/primitive"
	"go.mongodb.org/mongo-driver/mongo"
	"log"
	"receipt_collector/dispose"
)

type Repository struct {
	client *mongo.Client
}

//New creates receipts repository.
func New(client *mongo.Client) Repository {
	repository := Repository{client: client}
	return repository
}

func (r *Repository) GetByQueryStringFilter(ctx context.Context, userId string, filter string) ([]*Receipt, error) {
	c := r.getCollection()
	hex, err := primitive.ObjectIDFromHex(userId)
	if err != nil {
		log.Printf("Wrong userId format %v", err)
		return nil, err
	}
	f := bson.M{"$and": bson.A{
		bson.M{"owner": hex},
		bson.M{"query_string": bson.M{"$regex": filter}},
	}}
	cursor, err := c.Find(ctx, f)
	if err != nil {
		return nil, err
	}
	defer dispose.Dispose(func() error {
		return cursor.Close(ctx)
	}, "error while mongo cursor close")
	receipts, err := readReceipts(ctx, cursor)
	return receipts, err
}

func readReceipts(ctx context.Context, cursor *mongo.Cursor) ([]*Receipt, error) {
	var receipts = make([]*Receipt, 0, 0)
	for cursor.Next(ctx) {
		var receipt Receipt
		err := cursor.Decode(&receipt)
		if err != nil {
			return receipts, err
		}
		receipts = append(receipts, &receipt)
	}
	return receipts, nil
}

func (r *Repository) getCollection() *mongo.Collection {
	return r.client.Database("receipt_collection").Collection("receipt_requests")
}
