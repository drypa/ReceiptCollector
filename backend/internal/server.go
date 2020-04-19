package internal

import (
	"context"
	api "github.com/drypa/ReceiptCollector/api/internal"
)

type server struct {
	api.UnimplementedInternalApiServer
	processor *Processor
}

func newServer(p *Processor) server {
	return server{processor: p}
}

//GetLoginLink is an implementation of gRPC same name method.
func (s *server) GetLoginLink(ctx context.Context, in *api.GetLoginLinkRequest) (*api.LoginLinkResponse, error) {
	processor := *(s.processor)
	return processor.GetLoginLink(ctx, in)
}
