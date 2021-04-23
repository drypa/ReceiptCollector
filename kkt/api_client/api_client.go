package api_client

//ApiClient contains all API credentials.
type ApiClient interface {
	GetSecret() string
	GetSessionId() string
	GetRefreshToken() string
	GetId() string
	Refresh(newToken string, newSession string)
}
