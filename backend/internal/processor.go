package internal

import (
	"context"
	api "github.com/drypa/ReceiptCollector/api/internal"
)

type Processor interface {
	GetLoginLink(ctx context.Context, in *api.GetLoginLinkRequest) (*api.LoginLinkResponse, error)
}