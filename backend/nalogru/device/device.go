package device

import "go.mongodb.org/mongo-driver/bson/primitive"

type Device struct {
	ClientSecret string                                            `bson:"client_secret"`
	SessionId    string                                            `bson:"session_id"`
	RefreshToken string                                            `bson:"refresh_token"`
	Id           primitive.ObjectID                                `bson:"_id,omitempty"`
	Update       func(sessionId string, refreshToken string) error `bson:"-"`
	UserId       string                                            `bson:"user_id"`
	Phone        string                                            `bson:"phone"`
}
