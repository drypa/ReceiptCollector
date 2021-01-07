package waste

import (
	"context"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/bson/primitive"
	"go.mongodb.org/mongo-driver/mongo"
	"log"
	"receipt_collector/receipts"
	"sync"
	"time"
)

type Worker struct {
}
type User struct {
	Id primitive.ObjectID `bson:"_id,omitempty"`
}

//NewWorker constructs worker.
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
		//TODO: possible error in createWasteForUser handling(wg error handling)
		go createWasteForUser(ctx, user, mongoClient, &wg)
	}
	wg.Wait()
	return nil
}

func createWasteForUser(ctx context.Context, user User, client *mongo.Client, wg *sync.WaitGroup) error {
	collection := getReceiptsCollection(client)
	cursor, err := collection.Find(ctx, bson.D{{"owner", user.Id}})
	if err != nil {
		return err
	}
	for cursor.Next(ctx) {
		receipt := receipts.UsersReceipt{}
		err := cursor.Decode(&receipt)
		if err != nil {
			return err
		}
		err = createWasteIfNeeded(ctx, user, receipt, client)
		if err != nil {
			return err
		}
		log.Printf("User %s processing is done\n", user.Id.Hex())
	}

	wg.Done()
	return nil
}

func createWasteIfNeeded(ctx context.Context, user User, receipt receipts.UsersReceipt, client *mongo.Client) error {
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
		if receipt.Receipt != nil {
			waste.Sum = float32(receipt.Receipt.TotalSum) / 100.0
			t, err := time.Parse("2006-01-02T15:04:05", receipt.Receipt.DateTime)
			if err != nil {
				log.Printf("Error parsing Time: %s", receipt.Receipt.DateTime)
			}
			waste.Date = t

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
