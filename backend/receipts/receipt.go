package receipts

import (
	"go.mongodb.org/mongo-driver/bson/primitive"
	"receipt_collector/receipts/purchase"
)

type Receipt struct {
	DateTime             string              `json:"dateTime"`
	TotalSum             int32               `json:"totalSum"`
	RetailPlaceAddress   string              `json:"retailPlaceAddress"`
	UserInn              string              `json:"userInn"`
	Items                []purchase.Purchase `json:"items"`
	RawData              string              `json:"rawData"`
	Operator             string              `json:"operator"`
	Nds18                int32               `json:"nds18"`
	Nds10                int32               `json:"nds10"`
	User                 string              `json:"user"`
	CashTotalSum         int64               `json:"cashTotalSum"`
	EcashTotalSum        int64               `json:"ecashTotalSum"`
	FiscalSign           int64               `json:"fiscalSign"`
	FiscalDocumentNumber int64               `json:"fiscalDocumentNumber"`
}

//UsersReceipt is Receipt linked to user.
type UsersReceipt struct {
	*Receipt
	Id          primitive.ObjectID `bson:"_id,omitempty" json:"id"`
	Owner       primitive.ObjectID `json:"owner" bson:"owner"`
	QueryString string             `bson:"query_string" json:"queryString"`
	Deleted     bool               `json:"deleted" bson:"deleted"`
	TicketId    string             `json:"ticket_id" bson:"ticket_id"`
}

//RequestStatus - get receipt from nalog.ru API status.
type RequestStatus string

const (
	Undefined = RequestStatus("undefined")
	Error     = RequestStatus("error")
	Success   = RequestStatus("success")
	Requested = RequestStatus("requested")
	NotFound  = RequestStatus("not_found")
)
