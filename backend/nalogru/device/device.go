package device

import "go.mongodb.org/mongo-driver/bson/primitive"

//ApiClient contains all API credentials.
type ApiClient interface {
	GetSecret() string
	GetSessionId() string
	GetRefreshToken() string
	GetId() string
	Refresh(newToken string, newSession string)
}

//Device implement ApiClient interface with Mongo persistence.
type Device struct {
	ClientSecret string             `bson:"client_secret"`
	SessionId    string             `bson:"session_id"`
	RefreshToken string             `bson:"refresh_token"`
	Id           primitive.ObjectID `bson:"_id,omitempty"`
}

func (d Device) Refresh(newToken string, newSession string) {
	d.SessionId = newSession
	d.RefreshToken = newToken
}

func (d Device) GetId() string {
	return d.Id.Hex()
}

func (d Device) GetSecret() string {
	return d.ClientSecret
}

func (d Device) GetSessionId() string {
	return d.SessionId
}

func (d Device) GetRefreshToken() string {
	return d.RefreshToken
}
