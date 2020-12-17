package repository

import (
	"context"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
	"receipt_collector/dispose"
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

//All returns all devices.
func (r *Repository) All(ctx context.Context) ([]device.Device, error) {
	collection := r.getCollection()
	cursor, err := collection.Find(ctx, bson.D{})
	if err != nil {
		return nil, err
	}
	defer dispose.Dispose(func() error { return cursor.Close(ctx) }, "Cursor close error")
	return readDevices(cursor, ctx)

}

func (r *Repository) getCollection() *mongo.Collection {
	return r.m.Database("receipt_collection").Collection("devices")
}

func (r *Repository) Update(ctx context.Context, d *device.Device) error {
	collection := r.getCollection()
	filter := bson.M{"id": d.Id}
	_, err := collection.UpdateOne(ctx, filter, d)
	return err
}

func readDevices(cursor *mongo.Cursor, ctx context.Context) ([]device.Device, error) {
	var devices = make([]device.Device, 0, 0)
	for cursor.Next(ctx) {
		var device = device.Device{}
		err := cursor.Decode(&device)
		if err != nil {
			return nil, err
		}
		devices = append(devices, device)
	}
	return devices, nil

}
