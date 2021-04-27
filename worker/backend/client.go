package backend

import (
	"context"
	"errors"
	inside "github.com/drypa/ReceiptCollector/api/inside"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials"
	"log"
)

type GrpcClient struct {
	client *inside.InternalApiClient
}

type ReceiptRequest struct {
	Id     string
	UserId string
	Qr     string
}

//NewClient creates instance of grpcClient.
func NewClient(backendUrl string, creds credentials.TransportCredentials) *GrpcClient {
	dial, err := grpc.Dial(backendUrl, grpc.WithTransportCredentials(creds))
	if err != nil {
		log.Printf("Failed to create connection with %s. Error: %v", backendUrl, err)
	}
	client := inside.NewInternalApiClient(dial)
	return &GrpcClient{client: &client}
}

//GetUnchekedQr return one not checked receipt qr code.
func (c *GrpcClient) GetUnchekedQr(ctx context.Context) (ReceiptRequest, error) {
	client := c.client
	res := ReceiptRequest{}
	request, err := (*client).GetFirstUnckeckedRequest(ctx, &inside.NoParams{})
	if err != nil {
		return res, err
	}
	res.Id = request.Id
	res.UserId = request.UserId
	res.Qr = request.Qr
	return res, nil
}

func (c *GrpcClient) UpdateStatus(ctx context.Context, request *ReceiptRequest, status int32) error {
	client := c.client
	in := inside.SetRequestStatusRequest{
		Id:     request.Id,
		Status: inside.Status(status),
	}
	response, err := (*client).SetRequestStatus(ctx, &in)
	if err != nil {
		return err
	}
	if response.Error != "" {
		return errors.New(response.Error)
	}
	return nil
}
