package backend

import (
	"context"
	"errors"
	inside "github.com/drypa/ReceiptCollector/api/inside"
	"github.com/drypa/ReceiptCollector/kkt"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials"
	"log"
)

type GrpcClient struct {
	client *inside.InternalApiClient
}

type ReceiptRequest struct {
	Id     string
	UserId string
	Qr     string
}

type Status int32

const (
	Undefined   Status = 0
	CheckPassed Status = 1
	CheckFailed Status = 2
	Requested   Status = 3
	Error       Status = 4
	NotFound    Status = 5
)

func (s Status) ToDto() inside.Status {
	return inside.Status(s)
}

//NewClient creates instance of grpcClient.
func NewClient(backendUrl string, creds credentials.TransportCredentials) *GrpcClient {
	dial, err := grpc.Dial(backendUrl, grpc.WithTransportCredentials(creds))
	if err != nil {
		log.Printf("Failed to create connection with %s. Error: %v", backendUrl, err)
	}
	client := inside.NewInternalApiClient(dial)
	return &GrpcClient{client: &client}
}

//GetUnchekedQr return one not checked receipt qr code.
func (c *GrpcClient) GetUnchekedQr(ctx context.Context) (*ReceiptRequest, error) {
	client := c.client
	res := ReceiptRequest{}
	request, err := (*client).GetFirstUnckeckedRequest(ctx, &inside.NoParams{})
	if err != nil {
		return &res, err
	}
	res.Id = request.Id
	res.UserId = request.UserId
	res.Qr = request.Qr
	return &res, nil
}

func (c *GrpcClient) UpdateStatus(ctx context.Context, request *ReceiptRequest, status Status) error {
	client := c.client
	in := inside.SetRequestStatusRequest{
		Id:     request.Id,
		Status: inside.Status(status),
	}
	response, err := (*client).SetRequestStatus(ctx, &in)
	if err != nil {
		return err
	}
	if response.Error != "" {
		return errors.New(response.Error)
	}
	return nil
}

func (c *GrpcClient) GetFirstByStatus(ctx context.Context, status Status) (*ReceiptRequest, error) {
	client := c.client

	in := inside.QueryByStatus{
		Status: status.ToDto(),
	}
	request, err := (*client).GetFirstRequestWithStatus(ctx, &in)
	if err != nil {
		return nil, err
	}
	if request == nil {
		return nil, nil
	}
	return mapRequest(request), nil

}

func (c *GrpcClient) SetTicketId(ctx context.Context, request *ReceiptRequest, ticketId string) error {
	client := c.client

	in := inside.SetTicketIdRequest{
		Id:       request.Id,
		TicketId: ticketId,
	}

	response, err := (*client).SetTicketId(ctx, &in)
	if err != nil {
		return err
	}
	if response.Error != "" {
		return errors.New(response.Error)
	}
	return nil
}

func (c *GrpcClient) AddRawTicket(ctx context.Context, raw *kkt.TicketDetails) error {
	client := c.client
	details := mapRawDetails(raw)
	in := inside.AddRawTicketRequest{
		Details: &details,
	}
	response, err := (*client).AddRawTicket(ctx, &in)

	if err != nil {
		return err
	}
	if response.Error != "" {
		return errors.New(response.Error)
	}
	return nil
}

func mapRawDetails(raw *kkt.TicketDetails) inside.TicketDetails {
	return inside.TicketDetails{
		Status:       uint32(raw.Status),
		Id:           raw.Id,
		Kind:         raw.Kind,
		CreatedAt:    raw.CreatedAt,
		Qr:           raw.Qr,
		Operation:    mapOperation(&raw.Operation),
		Process:      nil,
		Query:        mapQuery(raw.Query),
		Ticket:       mapTicket(raw.Ticket),
		Organization: mapOrganization(raw.Organization),
		Seller:       mapSeller(raw.Seller),
	}
}

func mapSeller(seller *kkt.Seller) *inside.Seller {
	return &inside.Seller{
		Name: seller.Name,
		Inn:  seller.Inn,
	}
}

func mapOrganization(organization *kkt.Organization) *inside.Organization {
	return &inside.Organization{
		Name: organization.Name,
		Inn:  organization.Inn,
	}
}

func mapQuery(query kkt.Query) *inside.Query {
	return &inside.Query{
		OperationType: uint32(query.OperationType),
		Sum:           uint64(query.Sum),
		DocumentId:    uint32(query.DocumentId),
		FsId:          query.FsId,
		FiscalSign:    query.FiscalSign,
		Date:          query.Date,
	}
}
func mapOperation(operation *kkt.Operation) *inside.Operation {
	return &inside.Operation{
		Date: operation.Date,
		Type: uint32(operation.Type),
		Sum:  uint64(operation.Sum),
	}
}

func mapTicket(ticket *kkt.Ticket) *inside.Ticket {
	return &inside.Ticket{
		Document: &inside.Document{
			Receipt: &inside.Receipt{
				DateTime:             ticket.Document.Receipt.DateTime,
				CashTotalSum:         ticket.Document.Receipt.CashTotalSum,
				Code:                 int32(ticket.Document.Receipt.Code),
				CreditSum:            ticket.Document.Receipt.CreditSum,
				EcashTotalSum:        ticket.Document.Receipt.EcashTotalSum,
				FiscalDocumentNumber: int32(ticket.Document.Receipt.FiscalDocumentNumber),
				FnsUrl:               ticket.Document.Receipt.FnsUrl,
				Items:                mapItems(ticket.Document.Receipt.Items),
				UserInn:              ticket.Document.Receipt.UserInn,
				Nds10:                ticket.Document.Receipt.Nds10,
				Nds18:                ticket.Document.Receipt.Nds18,
				OperationType:        int32(ticket.Document.Receipt.OperationType),
				Operator:             ticket.Document.Receipt.Operator,
				PrepaidSum:           ticket.Document.Receipt.PrepaidSum,
				ProvisionSum:         ticket.Document.Receipt.ProvisionSum,
				RequestNumber:        int32(ticket.Document.Receipt.RequestNumber),
				RetailPlace:          ticket.Document.Receipt.RetailPlace,
				RetailPlaceAddress:   ticket.Document.Receipt.RetailPlaceAddress,
				TotalSum:             ticket.Document.Receipt.TotalSum,
				User:                 ticket.Document.Receipt.User,
				PostpaymentSum:       0,  //TODO: need add to kkt model?
				CounterSubmissionSum: 0,  //TODO: need add to kkt model?
				FiscalDriveNumber:    "", //TODO: need add to kkt model?
				FiscalSign:           uint32(ticket.Document.Receipt.FiscalSign),
				KktRegId:             ticket.Document.Receipt.KktRegId,
				PrepaymentSum:        0,  //TODO: need add to kkt model?
				ProtocolVersion:      0,  //TODO: need add to kkt model?
				ReceiptCode:          0,  //TODO: need add to kkt model?
				SenderAddress:        "", //TODO: need add to kkt model?
				ShiftNumber:          0,  //TODO: need add to kkt model?
				TaxationType:         0,  //TODO: need add to kkt model?
			},
		},
	}
}

func mapItems(items []kkt.Item) []*inside.Item {
	res := make([]*inside.Item, len(items))
	for i, v := range items {
		res[i] = mapItem(v)
	}
	return res
}

func mapItem(item kkt.Item) *inside.Item {
	return &inside.Item{
		Name:                   item.Name,
		Nds:                    int32(item.Nds),
		NdsSum:                 item.NdsSum,
		PaymentType:            int32(item.PaymentType),
		Price:                  item.Price,
		Quantity:               item.Quantity,
		Sum:                    item.Sum,
		CalculationSubjectSign: 0,
		CalculationTypeSign:    0,
		NdsRate:                0, //TODO: need add to kkt model?
	}
}

func mapRequest(dto *inside.ReceiptRequest) *ReceiptRequest {
	return &ReceiptRequest{
		Id:     dto.Id,
		UserId: dto.UserId,
		Qr:     dto.Qr,
	}
}
