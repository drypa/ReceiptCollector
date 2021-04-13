package receipts

import (
	"context"
	"log"
	"os"
	"receipt_collector/mongo_client"
	"receipt_collector/nalogru"
	"testing"
)

var mongoURL = os.Getenv("MONGO_URL")
var mongoUser = os.Getenv("MONGO_LOGIN")
var mongoSecret = os.Getenv("MONGO_SECRET")

var details = nalogru.TicketDetails{
	Status:    1,
	Id:        "str",
	Kind:      "kind",
	CreatedAt: "2019-11-03T11:12:00+00:00",
	Qr:        "qr",
	Operation: nalogru.Operation{},
	Query:     nalogru.Query{},
}

//Test_InsertRawTicket_UpdateExisted checks InsertRawTicket method can add and update.
func IgnoreTest_InsertRawTicket_UpdateExisted(t *testing.T) {
	repository, err := createRepository()
	if err != nil {
		t.Fail()
	}
	if repository == nil {
		t.Fail()
		return //to suppress warning(repository may be nil)
	}

	err = repository.InsertRawTicket(context.Background(), &details)

	if err != nil {
		log.Println("Failed to add new raw receipt.")
		t.Fail()
	}

	err = repository.InsertRawTicket(context.Background(), &details)

	if err != nil {
		log.Println("Failed to update raw receipt.")
		t.Fail()
	}
}

func createRepository() (*Repository, error) {
	settings := mongo_client.NewSettings(mongoURL, mongoUser, mongoSecret)
	client, err := mongo_client.New(settings)

	if err != nil {
		log.Println("Failed to create mongo client.")
		return nil, err
	}
	repository := NewRepository(client)
	return &repository, err
}
