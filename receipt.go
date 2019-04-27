package main

import "fmt"

type Purchase struct {
	Price      int32      `json:"price"`
	Sum        int32      `json:"sum"`
	Quantity   float32    `json:"quantity"`
	Name       string     `json:"name"`
	Categories []Category `json:"categories"`
}

type Receipt struct {
	Id                   string     `bson:"_id,omitempty" json:"id"`
	DateTime             string     `json:"date_time"`
	TotalSum             int32      `json:"total_sum"`
	RetailPlaceAddress   string     `json:"retail_place_address"`
	UserInn              string     `json:"user_inn"`
	Items                []Purchase `json:"items"`
	RawData              string     `json:"raw_data"`
	Operator             string     `json:"operator"`
	Nds18                int32      `json:"nds_18"`
	Nds10                int32      `json:"nds_10"`
	User                 string     `json:"user"`
	CashTotalSum         int32      `json:"cash_total_sum"`
	EcashTotalSum        int32      `json:"ecash_total_sum"`
	FiscalSign           int64      `json:"fiscal_sign"`
	FiscalDocumentNumber int64      `json:"fiscal_document_number"`
}

func (purchase *Purchase) String() string {
	return fmt.Sprintf("Purchase: Name=%s; Price=%d; Quantity=%f; Sum=%d", purchase.Name, purchase.Price, purchase.Quantity, purchase.Sum)
}

func (receipt *Receipt) String() string {
	return fmt.Sprintf("Receipt: Date=%s; RetailAddress=%s; Inn=%s; ItemsCount=%d", receipt.DateTime, receipt.RetailPlaceAddress, receipt.UserInn, len(receipt.Items))
}

type Category string

const (
	Food          Category = "food"
	Alcohol       Category = "alcohol"
	Clothes       Category = "clothes"
	Shoes         Category = "shoes"
	Medicine      Category = "medicine"
	HomeAppliance Category = "home_appliance"
	Entertainment Category = "entertainment"
)
