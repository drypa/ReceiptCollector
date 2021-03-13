package internal

import (
	"context"
	api "github.com/drypa/ReceiptCollector/api/inside"
	"google.golang.org/grpc"
)

//AccountProcessor is an interface for Process bot requests.
type AccountProcessor interface {
	GetLoginLink(ctx context.Context, in *api.GetLoginLinkRequest) (*api.LoginLinkResponse, error)
}

type ReceiptProcessor interface {
	AddReceipt(ctx context.Context, in *api.AddReceiptRequest, opts ...grpc.CallOption) (*api.AddReceiptResponse, error)
}
