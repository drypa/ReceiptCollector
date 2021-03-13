package backend

import (
	"context"
	inside "github.com/drypa/ReceiptCollector/api/inside"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials"
	"log"
)

type GrpcClient struct {
	client *inside.InternalApiClient
}

//NewGrpcClient creates instance of grpcClient.
func NewGrpcClient(backendUrl string, creds credentials.TransportCredentials) *GrpcClient {
	dial, err := grpc.Dial(backendUrl, grpc.WithTransportCredentials(creds))
	if err != nil {
		log.Printf("Failed to create connection with %s. Error: %v", backendUrl, err)
	}
	client := inside.NewInternalApiClient(dial)
	return &GrpcClient{client: &client}
}

//GetLoginLink returns link to login for telegram user.
func (c *GrpcClient) GetLoginLink(ctx context.Context, telegramId int) (string, error) {
	client := c.client
	request := inside.GetLoginLinkRequest{TelegramId: int32(telegramId)}
	link, err := (*client).GetLoginLink(ctx, &request)
	if err != nil {
		return "", err
	}
	return link.Url, nil
}

//AddReceipt adds new receipt by bar code.
func (c *GrpcClient) AddReceipt(ctx context.Context, userId string, qr string) (statusMessage string, e error) {
	client := c.client
	in := inside.AddReceiptRequest{
		UserId:    userId,
		ReceiptQr: qr,
	}
	result, err := (*client).AddReceipt(ctx, &in)
	if err != nil {
		log.Printf("Add receipt error: %v\n", err)
		return "Failed to add new receipt", err
	}
	return result.Error, err

}
