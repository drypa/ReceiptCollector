package receipts

import (
	"context"
	api "github.com/drypa/ReceiptCollector/api/inside"
	"google.golang.org/grpc"
)

type Processor struct {
	r *Repository
}

//NewProcessor constructs Processor.
func NewProcessor(r *Repository) *Processor {
	return &Processor{r: r}
}

//AddReceipt is used to add new receipt by bar code.
func (p *Processor) AddReceipt(ctx context.Context, in *api.AddReceiptRequest, opts ...grpc.CallOption) (*api.AddReceiptResponse, error) {
	err := processReceiptQueryString(ctx, p.r, in.ReceiptQr, in.UserId)
	return &api.AddReceiptResponse{}, err
}
