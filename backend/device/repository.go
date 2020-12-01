package device

import (
	"context"
	"go.mongodb.org/mongo-driver/mongo"
	"receipt_collector/nalogru/device"
)

type Repository struct {
	m *mongo.Client
}

//NewRepository creates Repository.
func NewRepository(m *mongo.Client) *Repository {
	return &Repository{m: m}
}

func (r *Repository) Add(ctx context.Context, d device.Device) error {
	collection := r.getCollection()
	_, err := collection.InsertOne(ctx, d)
	return err
}

func (r *Repository) getCollection() *mongo.Collection {
	return r.m.Database("receipt_collection").Collection("devices")
}
