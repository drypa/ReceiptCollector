package internal

import (
	"context"
	api "github.com/drypa/ReceiptCollector/api/inside"
)

//Processor is an interface for Process bot requests.
type Processor interface {
	GetLoginLink(ctx context.Context, in *api.GetLoginLinkRequest) (*api.LoginLinkResponse, error)
}
