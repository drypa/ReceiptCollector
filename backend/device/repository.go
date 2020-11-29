package device

import "go.mongodb.org/mongo-driver/mongo"

type Repository struct {
	m *mongo.Client
}

//NewRepository creates Repository.
func NewRepository(m *mongo.Client) *Repository {
	return &Repository{m: m}
}
