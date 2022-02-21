package receipts

import (
	"context"
	api "github.com/drypa/ReceiptCollector/api/inside"
	"google.golang.org/grpc"
	"receipt_collector/receipts/purchase"
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

func (p *Processor) GetReceipts(in *api.GetReceiptsRequest, out api.ReceiptApi_GetReceiptsServer) error {
	//TODO: do not load all receipts. make streaming from cursor.
	receipts, err := p.r.GetByUser(out.Context(), in.UserId)
	if err != nil {
		return err
	}
	for _, v := range receipts {
		if v.Receipt != nil {
			contract := toContract(&v)
			err = out.Send(&contract)
			if err != nil {
				return err
			}
		}
	}
	return err
}

func toContract(receipt *UsersReceipt) api.Receipt {
	items := make([]*api.Item, len(receipt.Items))
	for i, v := range receipt.Items {
		contract := mapItemToContract(&v)
		items[i] = &contract
	}

	return api.Receipt{
		EcashTotalSum:        receipt.EcashTotalSum,
		FiscalDocumentNumber: int32(receipt.FiscalDocumentNumber),
		Items:                items,
		UserInn:              receipt.UserInn,
		Nds10:                int64(receipt.Nds10),
		Nds18:                int64(receipt.Nds18),
		Operator:             receipt.Operator,
		RetailPlaceAddress:   receipt.RetailPlaceAddress,
		TotalSum:             int64(receipt.TotalSum),
	}
}

func mapItemToContract(item *purchase.Purchase) api.Item {
	return api.Item{
		Name:     item.Name,
		Price:    int64(item.Price),
		Quantity: item.Quantity,
		Sum:      int64(item.Sum),
	}
}
