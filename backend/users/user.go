package users

import "go.mongodb.org/mongo-driver/bson/primitive"

type User struct {
	Id           primitive.ObjectID `json:"id" bson:"_id,omitempty"`
	Name         string             `json:"name" bson:"name"`
	PasswordHash string             `json:"-" bson:"password_hash"`
}

type UserRequest struct {
	Login    string `json:"login"`
	Password string `json:"password"`
}
