package device

type Device struct {
	ClientSecret string `bson:"client_secret"`
	SessionId    string `bson:"session_id"`
	RefreshToken string `bson:"refresh_token"`
	Id           string `bson:"id"`
}
