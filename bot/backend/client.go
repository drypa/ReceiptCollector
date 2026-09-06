package backend

import (
	"bytes"
	"encoding/json"
	"net/http"
)

// Client is service client for backend.
type Client struct {
	backendUrl string
	httpClient *http.Client
}

// New constructs new backend client.
func New(backendUrl string) Client {
	return Client{
		backendUrl: backendUrl,
		httpClient: &http.Client{
			// Внутренний сервис: прямой доступ, минуя прокси.
			// defense-in-depth: даже если HTTP_PROXY попадёт в окружение
			// (например, при локальном запуске на машине разработчика),
			// legacy-код не будет ходить через прокси (ADR-018).
			Transport: &http.Transport{
				Proxy: nil,
			},
		},
	}
}

func getReader(request interface{}) (*bytes.Reader, error) {
	requestBytes, err := json.Marshal(request)
	if err != nil {
		return nil, err
	}
	reader := bytes.NewReader(requestBytes)
	return reader, nil
}
