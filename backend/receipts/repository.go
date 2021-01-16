package receipts

import (
	"context"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/bson/primitive"
	"go.mongodb.org/mongo-driver/mongo"
	"receipt_collector/dispose"
	"receipt_collector/nalogru"
	"time"
)

const checkStatus = "check_request_status"

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
func (repository Repository) getRawTicketCollection() *mongo.Collection {
	return repository.client.
		Database("receipt_collection").
		Collection("raw_tickets")
}

func (repository Repository) Insert(ctx context.Context, receipt UsersReceipt) error {
	collection := repository.getCollection()

	_, err := collection.InsertOne(ctx, receipt)
	return err
}

func (repository *Repository) InsertRawTicket(ctx context.Context, details *nalogru.TicketDetails) error {
	collection := repository.getRawTicketCollection()

	_, err := collection.InsertOne(ctx, details)
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
	defer dispose.Dispose(func() error {
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

func (repository Repository) SetReceiptStatus(ctx context.Context, receiptId string, status RequestStatus) error {
	collection := repository.getCollection()

	id, err := primitive.ObjectIDFromHex(receiptId)
	if err != nil {
		return err
	}
	filter := bson.M{"_id": bson.M{"$eq": id}}
	update := bson.M{"$set": bson.M{checkStatus: status}}
	_, err = collection.UpdateOne(ctx, filter, update)
	return err
}

func (repository Repository) SetReceipt(ctx context.Context, id primitive.ObjectID, receipt Receipt) error {
	collection := repository.getCollection()

	filter := bson.M{"_id": id}
	update := bson.M{"$set": bson.M{"receipt": receipt}}
	_, err := collection.UpdateOne(ctx, filter, update)
	return err
}

func (repository Repository) UpdateCheckStatus(ctx context.Context, receipt UsersReceipt, status RequestStatus) error {
	collection := repository.getCollection()

	update := bson.M{
		"$set": bson.M{
			checkStatus:          status,
			"check_request_time": time.Now().UTC(),
		},
	}
	filter := bson.M{"_id": bson.M{"$eq": receipt.Id}}
	_, err := collection.UpdateOne(ctx, filter, update)
	return err
}

//GetWithoutCheckRequest returns first receipt without check performed.
func (repository Repository) GetWithoutCheckRequest(ctx context.Context) (*UsersReceipt, error) {
	collection := repository.getCollection()

	usersReceipt := UsersReceipt{}
	query := bson.M{"$or": []bson.M{
		{checkStatus: Undefined},
		{checkStatus: nil},
		{checkStatus: ""}}}

	err := collection.FindOne(ctx, query).Decode(&usersReceipt)

	if err == mongo.ErrNoDocuments {
		return nil, nil
	}
	return &usersReceipt, err
}

//GetWithoutTicket returns first user request without requested ticket.
func (repository *Repository) GetWithoutTicket(ctx context.Context) (*UsersReceipt, error) {
	collection := repository.getCollection()

	usersReceipt := UsersReceipt{}
	query := bson.D{{checkStatus, Success}}

	err := collection.FindOne(ctx, query).Decode(&usersReceipt)

	if err == mongo.ErrNoDocuments {
		return nil, nil
	}
	return &usersReceipt, err
}

func (repository *Repository) SetTicketId(ctx context.Context, receipt *UsersReceipt, ticketId string) error {
	collection := repository.getCollection()

	update := bson.M{
		"$set": bson.M{
			checkStatus: Requested,
			"ticket_id": ticketId,
		},
	}
	filter := bson.M{"_id": bson.M{"$eq": receipt.Id}}
	_, err := collection.UpdateOne(ctx, filter, update)
	return err

}
