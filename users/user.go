package users

import "go.mongodb.org/mongo-driver/bson/primitive"

type User struct {
	Id           primitive.ObjectID `json:"id" bson:"_id,omitempty"`
	Name         string             `json:"name" bson:"name"`
	PasswordHash string             `json:"-" bson:"password_hash"`
}

type RegistrationRequest struct {
	Name     string `json:"name"`
	Password string `json:"password"`
}
