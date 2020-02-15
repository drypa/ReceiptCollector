package users

import "go.mongodb.org/mongo-driver/bson/primitive"

//User presents application user.
type User struct {
	Id           primitive.ObjectID `json:"id" bson:"_id,omitempty"`
	Name         string             `json:"name" bson:"name"`
	PasswordHash string             `json:"-" bson:"password_hash"`
	TelegramId   int                `json:"telegram_id" bson:"telegram_id"`
}

//UserRequest - new user account request.
type UserRequest struct {
	Login    string `json:"login"`
	Password string `json:"password"`
}
