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
	internal *inside.InternalApiClient
	account  *inside.AccountApiClient
	receipt  *inside.ReceiptApiClient
}

// NewGrpcClient creates instance of grpcClient.
func NewGrpcClient(backendUrl string, creds credentials.TransportCredentials) *GrpcClient {
	dial, err := grpc.Dial(backendUrl, grpc.WithTransportCredentials(creds))
	if err != nil {
		log.Printf("Failed to create connection with %s. Error: %v", backendUrl, err)
	}
	internal := inside.NewInternalApiClient(dial)
	account := inside.NewAccountApiClient(dial)
	receipt := inside.NewReceiptApiClient(dial)
	return &GrpcClient{internal: &internal, account: &account, receipt: &receipt}
}

// GetLoginLink returns link to login for telegram user.
func (c *GrpcClient) GetLoginLink(ctx context.Context, telegramId int) (string, error) {
	client := c.account
	request := inside.GetLoginLinkRequest{TelegramId: int32(telegramId)}
	link, err := (*client).GetLoginLink(ctx, &request)
	if err != nil {
		return "", err
	}
	return link.Url, nil
}

// AddReceipt adds new receipt by bar code.
func (c *GrpcClient) AddReceipt(ctx context.Context, userId string, qr string) (statusMessage string, e error) {
	client := c.receipt
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

// GetUsers returns all users.
func (c *GrpcClient) GetUsers(ctx context.Context) ([]User, error) {
	client := c.account

	resp, err := (*client).GetUsers(ctx, &inside.NoParams{})
	if err != nil {
		return nil, err
	}
	result := make([]User, len(resp.Users))
	for i, v := range resp.Users {
		result[i] = User{
			UserId:     v.UserId,
			TelegramId: int(v.TelegramId),
		}
	}

	return result, nil

}

// GetUser returns user by telegramId.
func (c *GrpcClient) GetUser(ctx context.Context, telegramId int) (*User, error) {
	client := c.account

	in := inside.GetUserRequest{
		TelegramId: int32(telegramId),
	}
	resp, err := (*client).GetUser(ctx, &in)

	if err != nil {
		return nil, err
	}

	if resp.User == nil {
		log.Printf("User with id %d  not found\n", telegramId)
		return nil, errors.New("not_found")
	}

	user := User{
		UserId:     resp.User.UserId,
		TelegramId: int(resp.User.TelegramId),
	}
	return &user, err
}

// GetReceiptReport return receipt details as file.
func (c *GrpcClient) GetReceiptReport(ctx context.Context, userId string, qr string) ([]byte, string, error) {
	client := c.receipt

	in := inside.GetRawReceiptReportRequest{
		UserId: userId,
		Qr:     qr,
	}
	resp, err := (*client).GetRawReceipt(ctx, &in)

	if err != nil {
		return nil, "", err
	}

	if resp == nil {
		return nil, "", errors.New("not_found")
	}

	return resp.Report, resp.FileName, err
}
