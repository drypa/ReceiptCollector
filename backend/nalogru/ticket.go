package nalogru

type Ticket struct {
	Status    int    `json:"status"`
	Id        string `json:"id"`
	Kind      string `json:"kind"`
	CreatedAt string `json:"createdAt"`
	Qr        string
	Operation Operation `json:"operation"`
	Query     Query     `json:"query"`
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
