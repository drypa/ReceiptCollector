package waste

import (
	"context"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/bson/primitive"
	"go.mongodb.org/mongo-driver/mongo"
	"sync"
)

type Worker struct {
}
type User struct {
	Id primitive.ObjectID `bson:"_id,omitempty"`
}
type UsersReceipt struct {
	Id primitive.ObjectID `bson:"_id,omitempty"`
}

func NewWorker() Worker {
	return Worker{}
}

func (worker Worker) Process(ctx context.Context, mongoClient *mongo.Client) error {
	collection := getUsersCollection(mongoClient)
	cursor, err := collection.Find(ctx, bson.D{})
	if err != nil {
		return err
	}
	wg := sync.WaitGroup{}
	for cursor.Next(ctx) {
		user := User{}
		err := cursor.Decode(&user)
		if err != nil {
			return err
		}
		wg.Add(1)
		go createWasteForUser(ctx, user, mongoClient, wg)
	}
	wg.Wait()
	return nil
}

func createWasteForUser(ctx context.Context, user User, client *mongo.Client, wg sync.WaitGroup) error {
	collection := getReceiptsCollection(client)
	cursor, err := collection.Find(ctx, bson.D{{"owner", user.Id}})
	if err != nil {
		return err
	}
	for cursor.Next(ctx) {
		receipt := UsersReceipt{}
		err := cursor.Decode(&receipt)
		if err != nil {
			return err
		}
		createWasteIfNeeded(ctx, user, receipt, client)
	}

	wg.Done()
}

func createWasteIfNeeded(ctx context.Context, user User, receipt UsersReceipt, client *mongo.Client) error {
	repository := NewRepository(client)
	wastes, err := repository.GetForUser(ctx, user.Id.Hex())
	if err != nil {
		return err
	}

	wastesMap := MapByReceipt(wastes)

	existWaste := wastesMap[receipt.Id.Hex()]
	if existWaste == nil {
		waste := Waste{
			ReceiptId: receipt.Id.Hex(),
			OwnerId:   user.Id.Hex(),
		}
		err := repository.Add(ctx, waste)
		if err != nil {
			return err
		}
	}
	return nil
}

func getUsersCollection(mongoClient *mongo.Client) *mongo.Collection {
	return mongoClient.Database("receipt_collection").Collection("system_users")
}

func getReceiptsCollection(mongoClient *mongo.Client) *mongo.Collection {
	return mongoClient.Database("receipt_collection").Collection("receipt_requests")
}
