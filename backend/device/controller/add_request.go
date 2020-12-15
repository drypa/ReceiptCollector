package controller

type AddRequest struct {
	ClientSecret string `json:"client_secret"`
	SessionId    string `json:"session_id"`
	RefreshToken string `json:"refresh_token"`
}
