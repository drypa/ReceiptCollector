package receipts

import (
	"context"
	"errors"
	"fmt"
	api "github.com/drypa/ReceiptCollector/api/inside"
	"google.golang.org/grpc"
	"receipt_collector/receipts/purchase"
	"receipt_collector/render"
)

type Processor struct {
	repo   *Repository
	render *render.Render
}

// NewProcessor constructs Processor.
func NewProcessor(r *Repository, render *render.Render) *Processor {
	return &Processor{repo: r, render: render}
}

// AddReceipt is used to add new receipt by bar code.
func (p *Processor) AddReceipt(ctx context.Context, in *api.AddReceiptRequest, _ ...grpc.CallOption) (*api.AddReceiptResponse, error) {
	err := processReceiptQueryString(ctx, p.repo, in.ReceiptQr, in.UserId)
	return &api.AddReceiptResponse{}, err
}

func (p *Processor) GetReceipts(in *api.GetReceiptsRequest, out api.ReceiptApi_GetReceiptsServer) error {
	//TODO: do not load all receipts. make streaming from cursor.
	receipts, err := p.repo.GetByUser(out.Context(), in.UserId)
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
func (p *Processor) GetRawReceipt(ctx context.Context, in *api.GetRawReceiptReportRequest) (*api.RawReceiptReport, error) {
	qr, err := normalize(in.Qr)
	if err != nil {
		return nil, err
	}
	receipt, err := p.repo.GetByQueryString(ctx, in.UserId, qr)
	if err != nil {
		return nil, err
	}
	if receipt == nil {
		return nil, errors.New("not found")
	}
	r, err := p.repo.GetRawReceipt(ctx, qr)

	bytes, err := p.render.Receipt(r.Ticket.Document.Receipt)
	return &api.RawReceiptReport{
		Report:   bytes,
		FileName: fmt.Sprintf("%s_%s.html", r.Seller.Name, r.Query.Date),
	}, err
}

func toContract(receipt *UsersReceipt) api.Receipt {
	items := make([]*api.Item, len(receipt.Items))
	for i, v := range receipt.Items {
		contract := mapItemToContract(&v)
		items[i] = &contract
	}

	return api.Receipt{
		Id:                   receipt.Id.Hex(),
		DateTime:             receipt.GetDate().UnixMilli(),
		CashTotalSum:         receipt.CashTotalSum,
		EcashTotalSum:        receipt.EcashTotalSum,
		FiscalDocumentNumber: int32(receipt.FiscalDocumentNumber),
		Items:                items,
		UserInn:              receipt.UserInn,
		Nds10:                int64(receipt.Nds10),
		Nds18:                int64(receipt.Nds18),
		Operator:             receipt.Operator,
		RetailPlaceAddress:   receipt.RetailPlaceAddress,
		TotalSum:             int64(receipt.TotalSum),
		User:                 receipt.User,
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
