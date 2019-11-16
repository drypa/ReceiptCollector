package receipts

import (
	"context"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/bson/primitive"
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
	return repository.client.Database("receipt_collection").Collection("receipt_requests")
}

func (repository Repository) Insert(ctx context.Context, receipt UsersReceipt) error {
	collection := repository.getCollection()

	_, err := collection.InsertOne(ctx, receipt)
	return err
}

func (repository Repository) GetByUser(ctx context.Context, userId string) ([]UsersReceipt, error) {
	collection := repository.getCollection()

	id, err := primitive.ObjectIDFromHex(userId)
	if err != nil {
		return nil, err
	}
	cursor, err := collection.Find(ctx, bson.D{{"owner", id}})
	if err != nil {
		return nil, err
	}
	defer utils.Dispose(func() error {
		return cursor.Close(ctx)
	}, "error while mongo cursor close")
	receipts := readReceipts(cursor, ctx)
	return receipts, nil
}

func (repository Repository) FindOneOdfsRequestedWithoutReceipt(ctx context.Context) *UsersReceipt {
	collection := repository.getCollection()
	request := UsersReceipt{}
	err := collection.FindOne(ctx, bson.M{"$and": []bson.M{
		{"odfs_requested": bson.M{"$eq": true}},
		{"receipt": bson.M{"$eq": nil}}},
	}).Decode(&request)

	if err == mongo.ErrNoDocuments {
		return nil
	}
	return &request
}

func (repository Repository) SetReceipt(ctx context.Context, id primitive.ObjectID, receipt Receipt) error {
	collection := repository.getCollection()

	filter := bson.M{"_id": bson.M{"$eq": id}}
	update := bson.M{"$set": bson.M{"receipt": receipt}}
	_, err := collection.UpdateOne(ctx, filter, update)
	return err
}
