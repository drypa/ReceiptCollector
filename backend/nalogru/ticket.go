package nalogru

type TicketDetails struct {
	Status    int    `json:"status"`
	Id        string `json:"id"`
	Kind      string `json:"kind"`
	CreatedAt string `json:"createdAt"`
	Qr        string
	Operation Operation `json:"operation"`
	Query     Query     `json:"query"`
	Ticket    *Ticket   `json:"ticket"`
	Seller    *Seller
}

type Operation struct {
	Date string `json:"date"`
	Type int    `json:"type"`
	Sum  int64  `json:"sum"`
}

type Query struct {
	OperationType int    `json:"operationType"`
	Sum           int64  `json:"sum"`
	DocumentId    int    `json:"documentId"`
	FsId          string `json:"fsId"`
	FiscalSign    string `json:"fiscalSign"`
	Date          string `json:"date"`
}

type Seller struct {
	Name string `json:"name"`
	Inn  string `json:"inn"`
}

type Ticket struct {
	Document *Document `json:"document"`
}

type Document struct {
	Receipt *Receipt `json:"receipt"`
}

type Receipt struct {
	DateTime                int64  `json:"dateTime"`
	CashTotalSum            int    `json:"cashTotalSum"`
	Code                    int    `json:"code"`
	CreditSum               int    `json:"creditSum"`
	EcashTotalSum           int    `json:"ecashTotalSum"`
	FiscalDocumentFormatVer int    `json:"fiscalDocumentFormatVer"`
	FiscalDocumentNumber    int    `json:"fiscalDocumentNumber"`
	FiscalDriveNumber       int64  `json:"fiscalDriveNumber"`
	FiscalSign              int64  `json:"fiscalSign"`
	FnsUrl                  string `json:"fnsUrl"`
	Items                   []Item `json:"items"`
	KktRegId                string `json:"kktRegId"`
	Nds10                   int64  `json:"nds10"`
	Nds18                   int64  `json:"nd18"`
	OperationType           int    `json:"operationType"`
	Operator                string `json:"operator"`
	PrepaidSum              int64  `json:"prepaidSum"`
	ProvisionSum            int64  `json:"provisionSum"`
	RequestNumber           int    `json:"requestNumber"`
	RetailPlace             string `json:"retailPlace"`
	RetailPlaceAddress      string `json:"retailPlaceAddress"`
	ShiftNumber             int    `json:"shiftNumber"`
	TotalSum                int64  `json:"totalSum"`
	User                    string `json:"user"`
	UserInn                 string `json:"userInn"`
}

type Item struct {
	Name        string  `json:"name"`
	Nds         int     `json:"nds"`
	NdsSum      int64   `json:"ndsSum"`
	PaymentType int     `json:"paymentType"`
	Price       int64   `json:"price"`
	ProductType int     `json:"productType"`
	Quantity    float32 `json:"quantity"`
	Sum         int64   `json:"sum"`
}
