package receipts

import (
	"context"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/bson/primitive"
	"go.mongodb.org/mongo-driver/mongo"
	"go.mongodb.org/mongo-driver/mongo/options"
	"receipt_collector/dispose"
	"receipt_collector/nalogru"
	"time"
)

const checkStatus = "check_request_status"

type Repository struct {
	client *mongo.Client
}

// NewRepository creates receipts repository.
func NewRepository(client *mongo.Client) Repository {
	repository := Repository{client: client}
	return repository
}

func (repository *Repository) getCollection() *mongo.Collection {
	return repository.client.Database("receipt_collection").Collection("receipt_requests")
}
func (repository *Repository) getRawTicketCollection() *mongo.Collection {
	return repository.client.
		Database("receipt_collection").
		Collection("raw_tickets")
}

// Insert - add new user receipt to collection.
func (repository *Repository) Insert(ctx context.Context, receipt UsersReceipt) error {
	collection := repository.getCollection()

	_, err := collection.InsertOne(ctx, receipt)
	return err
}

// InsertRawTicket - add new raw ticket to collection.
func (repository *Repository) InsertRawTicket(ctx context.Context, details *nalogru.TicketDetails) error {
	collection := repository.getRawTicketCollection()

	filter := bson.M{"id": details.Id}
	opts := options.Update().SetUpsert(true)
	update := bson.M{"$set": details}
	_, err := collection.UpdateOne(ctx, filter, update, opts)
	return err
}

// GetByUser returns all user receipts for user.
func (repository *Repository) GetByUser(ctx context.Context, userId string) ([]UsersReceipt, error) {
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

// Delete marks user receipt as deleted.
func (repository *Repository) Delete(ctx context.Context, userId string, receiptId string) error {
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

// GetByQueryString find user receipt by QR code query string.
func (repository *Repository) GetByQueryString(ctx context.Context, userId string, queryString string) (*UsersReceipt, error) {
	collection := repository.getCollection()

	ownerId, err := primitive.ObjectIDFromHex(userId)
	if err != nil {
		return nil, err
	}

	query := bson.D{{"owner", ownerId}, {"query_string", queryString}}

	result := collection.FindOne(ctx, query)
	err = result.Err()
	if err != nil {
		return nil, err
	}

	receipt := UsersReceipt{}
	err = result.Decode(&receipt)

	return &receipt, err

}

// GetAllOwnersByQueryString find user receipt by QR code query string.
func (repository *Repository) GetAllOwnersByQueryString(ctx context.Context, queryString string) (*UsersReceipt, error) {
	collection := repository.getCollection()

	query := bson.D{{"query_string", queryString}}

	result := collection.FindOne(ctx, query)
	err := result.Err()
	if err != nil {
		return nil, err
	}

	receipt := UsersReceipt{}
	err = result.Decode(&receipt)

	return &receipt, err
}

// GetById returns receipt by it's id.
func (repository *Repository) GetById(ctx context.Context, userId string, receiptId string) (UsersReceipt, error) {
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

// SetReceiptStatus updates status of receipt request.
func (repository *Repository) SetReceiptStatus(ctx context.Context, receiptId string, status RequestStatus) error {
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

// UpdateCheckStatus set check receipt status.
func (repository *Repository) UpdateCheckStatus(ctx context.Context, receipt UsersReceipt, status RequestStatus) error {
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

// GetWithoutCheckRequest returns first receipt without check performed.
func (repository *Repository) GetWithoutCheckRequest(ctx context.Context) (*UsersReceipt, error) {
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

// GetWithoutTicket returns first user request without requested ticket.
func (repository *Repository) GetWithoutTicket(ctx context.Context) (*UsersReceipt, error) {
	collection := repository.getCollection()

	usersReceipt := UsersReceipt{}
	query := bson.M{
		"$and": bson.A{
			bson.M{"check_request_status": bson.M{"$ne": Error}},
			bson.M{"$or": bson.A{
				bson.M{"ticket_id": ""},
				bson.M{"ticket_id": nil},
			}}}}

	err := collection.FindOne(ctx, query).Decode(&usersReceipt)

	if err == mongo.ErrNoDocuments {
		return nil, nil
	}
	return &usersReceipt, err
}

// SetTicketId set ticket id in user receipt request collection.
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

// GetRawReceipt return receipt by qr.
func (repository *Repository) GetRawReceipt(ctx context.Context, qr string) (*nalogru.TicketDetails, error) {
	collection := repository.getRawTicketCollection()
	query := bson.M{"qr": qr}

	res := collection.FindOne(ctx, query)
	err := res.Err()

	if err == mongo.ErrNoDocuments {
		return nil, nil
	}

	if err != nil {
		return nil, err
	}
	details := &nalogru.TicketDetails{}
	err = res.Decode(details)
	return details, err
}

// GetRawReceiptWithoutTicket return first receipt without ticket body.
func (repository *Repository) GetRawReceiptWithoutTicket(ctx context.Context) (*nalogru.TicketDetails, error) {
	collection := repository.getRawTicketCollection()
	query := bson.M{"ticket": nil}

	res := collection.FindOne(ctx, query)
	err := res.Err()

	if err == mongo.ErrNoDocuments {
		return nil, nil
	}

	if err != nil {
		return nil, err
	}
	details := &nalogru.TicketDetails{}
	err = res.Decode(details)
	return details, err
}
