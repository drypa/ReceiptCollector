package internal

import (
	"context"
	api "github.com/drypa/ReceiptCollector/api/inside"
	"google.golang.org/grpc"
)

//AccountProcessor is an interface for Process account requests.
type AccountProcessor interface {
	GetLoginLink(ctx context.Context, in *api.GetLoginLinkRequest) (*api.LoginLinkResponse, error)
	GetUsers(ctx context.Context, req *api.NoParams) (*api.GetUsersResponse, error)
	GetUser(ctx context.Context, in *api.GetUserRequest, opts ...grpc.CallOption) (*api.GetUserResponse, error)
}

//ReceiptProcessor is an interface for process receipt requests.
type ReceiptProcessor interface {
	AddReceipt(ctx context.Context, in *api.AddReceiptRequest, opts ...grpc.CallOption) (*api.AddReceiptResponse, error)
}
