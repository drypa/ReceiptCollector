package workers

import (
	"context"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
	"log"
	"receipt_collector/nalogru"
	"receipt_collector/receipts"
	"time"
)

func OdfsWorkerStart(ctx context.Context, nalogruClient nalogru.Client, mongoClient *mongo.Client, settings Settings) {
	ticker := time.NewTicker(settings.Interval)

	for {
		select {
		case <-ctx.Done():
			log.Println("Odfs request worker finished")
			return
		case <-ticker.C:
			hour := time.Now().Hour()
			if hour >= settings.Start || hour <= settings.End {
				processRequests(ctx, nalogruClient, mongoClient)
			} else {
				log.Print("Not Yet. Odfs request delayed.")
				break
			}
			break
		}
	}

}

func processRequests(ctx context.Context, nalogruClient nalogru.Client, mongoClient *mongo.Client) {
	collection := mongoClient.Database("receipt_collection").Collection("receipt_requests")
	usersReceipt := receipts.UsersReceipt{}
	//TODO: move to repository
	query := bson.M{"$or": []bson.M{{"odfs_request_status": receipts.Undefined}, {"odfs_request_status": nil}}}

	err := collection.FindOne(ctx, query).Decode(&usersReceipt)
	if err == mongo.ErrNoDocuments {
		log.Println("No Odfs requests required")
		return
	}
	if err != nil {
		log.Printf("error while fetch unprocessed user requests. %s \n", err)
		return
	}
	err = nalogruClient.SendOdfsRequest(usersReceipt.QueryString)
	check(err)
	//TODO: move to repository
	update := bson.M{
		"$set": bson.M{
			"odfs_request_status": receipts.Success,
			"odfs_request_time":   time.Now(),
		},
	}
	filter := bson.M{"_id": bson.M{"$eq": usersReceipt.Id}}
	_, err = collection.UpdateOne(ctx, filter, update)
	check(err)
}

func check(err error) {
	if err != nil {
		log.Printf("Error occured %v", err)
		panic(err)
	}
}
