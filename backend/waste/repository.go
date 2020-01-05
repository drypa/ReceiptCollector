package waste

import (
	"errors"
	"go.mongodb.org/mongo-driver/mongo"
)

type Repository struct {
	client *mongo.Client
}

func NewRepository(client *mongo.Client) Repository {
	repository := Repository{client: client}
	return repository
}

func (repository Repository) AddByReceipt(query Query, receiptId string) error {
	//TODO: implement this!
	return errors.New("not implemented")
}

//TODO: add manually(name, sum, description, category)
