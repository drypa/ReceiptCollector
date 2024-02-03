package internal

import (
	"context"
	api "github.com/drypa/ReceiptCollector/api/inside"
	"google.golang.org/grpc"
)

// AccountProcessor is an interface for Process account requests.
type AccountProcessor interface {
	GetLoginLink(ctx context.Context, in *api.GetLoginLinkRequest) (*api.LoginLinkResponse, error)
	GetUsers(ctx context.Context, req *api.NoParams) (*api.GetUsersResponse, error)
	GetUser(ctx context.Context, in *api.GetUserRequest, opts ...grpc.CallOption) (*api.GetUserResponse, error)
	RegisterUser(ctx context.Context, in *api.UserRegistrationRequest, opts ...grpc.CallOption) (*api.UserRegistrationResponse, error)
	VerifyPhone(ctx context.Context, req *api.PhoneVerificationRequest) (*api.ErrorResponse, error)
}

// ReceiptProcessor is an interface for process receipt requests.
type ReceiptProcessor interface {
	AddReceipt(ctx context.Context, in *api.AddReceiptRequest, opts ...grpc.CallOption) (*api.AddReceiptResponse, error)
	GetReceipts(*api.GetReceiptsRequest, api.ReceiptApi_GetReceiptsServer) error
	GetRawReceipt(ctx context.Context, in *api.GetRawReceiptReportRequest) (*api.RawReceiptReport, error)
}

// ReportProcessor is an interface to send notifications to clients.
type ReportProcessor interface {
	GetReports(*api.NoParams, api.ReportApi_GetReportsServer) error
}
