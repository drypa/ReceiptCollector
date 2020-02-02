package backend

import (
	"bytes"
	"encoding/json"
)

type Client struct {
	backendUrl string
}

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
