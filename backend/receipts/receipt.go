package receipts

import (
	"encoding/json"
	"go.mongodb.org/mongo-driver/bson/primitive"
	"receipt_collector/receipts/purchase"
	"time"
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
	CashTotalSum         int32               `json:"cashTotalSum"`
	EcashTotalSum        int32               `json:"ecashTotalSum"`
	FiscalSign           int64               `json:"fiscalSign"`
	FiscalDocumentNumber int64               `json:"fiscalDocumentNumber"`
}

//UsersReceipt is Receipt linked to user.
type UsersReceipt struct {
	*Receipt
	Id                primitive.ObjectID `bson:"_id,omitempty" json:"id"`
	Owner             primitive.ObjectID `json:"owner" bson:"owner"`
	OdfsRequestTime   time.Time          `json:"odfsRequestTime" bson:"odfs_request_time"`
	KktRequestTime    time.Time          `json:"kktRequestTime" bson:"kkt_request_time"`
	QueryString       string             `bson:"query_string" json:"queryString"`
	OdfsRequested     bool               `json:"odfsRequested" bson:"odfs_requested"`
	OdfsRequestStatus RequestStatus      `json:"odfsRequestStatus" bson:"odfs_request_status,omitempty"`
	KktsRequestStatus RequestStatus      `json:"kktsRequestStatus" bson:"kkts_request_status,omitempty"`
	Deleted           bool               `json:"deleted" bson:"deleted"`
	TicketId          string             `json:"ticket_id" bson:"ticket_id"`
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

//ParseReceipt parse API response as Receipt.
func ParseReceipt(bytes []byte) (Receipt, error) {
	var receipt map[string]map[string]Receipt
	err := json.Unmarshal(bytes, &receipt)
	if err != nil {
		return Receipt{}, err
	}
	res := receipt["document"]["receipt"]
	return res, nil
}
