package backend

import (
	"bytes"
	"encoding/json"
)

//Client is service client for backend.
type Client struct {
	backendUrl string
}

//New constructs new backend client.
func New(backendUrl string) Client {
	return Client{backendUrl: backendUrl}
}

func getReader(request interface{}) (*bytes.Reader, error) {
	requestBytes, err := json.Marshal(request)
	if err != nil {
		return nil, err
	}
	reader := bytes.NewReader(requestBytes)
	return reader, nil
}
