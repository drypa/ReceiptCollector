package internal

import (
	"context"
	api "github.com/drypa/ReceiptCollector/api/inside"
)

type server struct {
	api.UnimplementedInternalApiServer
	accountProcessor *AccountProcessor
	receiptProcessor *ReceiptProcessor
}

func newServer(p *AccountProcessor, r *ReceiptProcessor) server {
	return server{accountProcessor: p, receiptProcessor: r}
}

// GetLoginLink is an implementation of gRPC same name method.
func (s *server) GetLoginLink(ctx context.Context, in *api.GetLoginLinkRequest) (*api.LoginLinkResponse, error) {
	processor := *(s.accountProcessor)
	return processor.GetLoginLink(ctx, in)
}

// GetUsers returns all existing user accounts.
func (s *server) GetUsers(ctx context.Context, req *api.NoParams) (*api.GetUsersResponse, error) {
	processor := *(s.accountProcessor)
	return processor.GetUsers(ctx, req)
}

// AddReceipt add receipt.
func (s *server) AddReceipt(ctx context.Context, req *api.AddReceiptRequest) (*api.AddReceiptResponse, error) {
	processor := *(s.receiptProcessor)
	return processor.AddReceipt(ctx, req)
}

// GetReceipts returns all receipts for user.
func (s *server) GetReceipts(in *api.GetReceiptsRequest, stream api.ReceiptApi_GetReceiptsServer) error {
	processor := *(s.receiptProcessor)
	return processor.GetReceipts(in, stream)
}

// GetUser get user by telegramId.
func (s *server) GetUser(ctx context.Context, in *api.GetUserRequest) (*api.GetUserResponse, error) {
	processor := *(s.accountProcessor)
	return processor.GetUser(ctx, in)
}

// GetRawReceipt returns raw receipt representation.
func (s *server) GetRawReceipt(ctx context.Context, in *api.GetRawReceiptReportRequest) (*api.RawReceiptReport, error) {
	processor := *(s.receiptProcessor)
	return processor.GetRawReceipt(ctx, in)
}

func (s *server) RegisterUser(ctx context.Context, req *api.UserRegistrationRequest) (*api.UserRegistrationResponse, error) {
	processor := *(s.accountProcessor)
	return processor.RegisterUser(ctx, req)
}
