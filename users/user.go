package users

import "go.mongodb.org/mongo-driver/bson/primitive"

type User struct {
	Id           primitive.ObjectID `json:"id" bson:"_id,omitempty"`
	Name         string             `json:"name"`
	PasswordHash string             `json:"-"`
}

type RegistrationRequest struct {
	Name     string `json:"name"`
	Password string `json:"password"`
}
