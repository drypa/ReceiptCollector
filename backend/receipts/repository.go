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

func readReceipts(cursor *mongo.Cursor, context context.Context) []UsersReceipt {
	var receipts = make([]UsersReceipt, 0, 0)
	for cursor.Next(context) {
		var receipt UsersReceipt
		err := cursor.Decode(&receipt)
		check(err)
		receipts = append(receipts, receipt)
	}
	return receipts
}

func (repository Repository) Delete(ctx context.Context, userId string, receiptId string) error {
	collection := repository.getCollection()
	id, err := primitive.ObjectIDFromHex(receiptId)
	if err != nil {
		return err
	}
	ownerId, err := primitive.ObjectIDFromHex(userId)
	if err != nil {
		return err
	}
	filter := bson.D{{"owner", ownerId}, {"_id", id}}
	update := bson.M{"$set": bson.M{"deleted": true}}
	_, err = collection.UpdateOne(ctx, filter, update)
	return err
}

func (repository Repository) GetByQueryString(ctx context.Context, userId string, queryString string) (*UsersReceipt, error) {
	collection := repository.getCollection()

	ownerId, err := primitive.ObjectIDFromHex(userId)
	if err != nil {
		return nil, err
	}

	query := bson.D{{"owner", ownerId}, {"query_string", queryString}}

	result := collection.FindOne(ctx, query)
	if result.Err() != nil {
		return nil, err
	}

	receipt := UsersReceipt{}
	err = result.Decode(&receipt)

	return &receipt, err

}

func (repository Repository) GetById(ctx context.Context, userId string, receiptId string) (UsersReceipt, error) {
	receipt := UsersReceipt{}
	collection := repository.getCollection()

	ownerId, err := primitive.ObjectIDFromHex(userId)
	if err != nil {
		return receipt, err
	}
	id, err := primitive.ObjectIDFromHex(receiptId)
	if err != nil {
		return receipt, err
	}

	query := bson.D{{"owner", ownerId}, {"_id", id}}

	result := collection.FindOne(ctx, query)
	if result.Err() != nil {
		return receipt, err
	}
	err = result.Decode(&receipt)
	return receipt, err
}

func (repository Repository) FindOneOdfsRequestedWithoutReceipt(ctx context.Context) *UsersReceipt {
	collection := repository.getCollection()
	request := UsersReceipt{}
	err := collection.FindOne(ctx, bson.M{"$and": []bson.M{
		{"odfs_requested": bson.M{"$eq": true}},
		{"receipt": bson.M{"$eq": nil}},
		{"$or": []bson.M{{"deleted": nil}, {"deleted": false}}},
	},
	}).Decode(&request)

	if err == mongo.ErrNoDocuments {
		return nil
	}
	return &request
}

func (repository Repository) ResetOdfsRequestForReceipt(ctx context.Context, receiptId string) error {
	collection := repository.getCollection()

	id, err := primitive.ObjectIDFromHex(receiptId)
	if err != nil {
		return err
	}
	filter := bson.M{"_id": bson.M{"$eq": id}}
	update := bson.M{"$set": bson.M{"odfs_requested": false}}
	_, err = collection.UpdateOne(ctx, filter, update)
	return err
}

func (repository Repository) SetReceipt(ctx context.Context, id primitive.ObjectID, receipt Receipt) error {
	collection := repository.getCollection()

	filter := bson.M{"_id": bson.M{"$eq": id}}
	update := bson.M{"$set": bson.M{"receipt": receipt}}
	_, err := collection.UpdateOne(ctx, filter, update)
	return err
}
