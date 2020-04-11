package internal

import (
	"context"
	"errors"
	api "github.com/drypa/ReceiptCollector/api/internal"
	"google.golang.org/grpc"
)

type server struct {
	api.UnimplementedInternalApiServer
}

//GetLoginLink is an implementation of gRPC method with same name.
func (s *server) GetLoginLink(ctx context.Context, in *api.GetLoginLinkRequest, opts ...grpc.CallOption) (*api.LoginLinkResponse, error) {
	return nil, errors.New("not implemented")
}
