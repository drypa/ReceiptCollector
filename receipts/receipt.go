package receipts

import (
	"fmt"
	"go.mongodb.org/mongo-driver/bson/primitive"
)

type Purchase struct {
	Price      int32      `json:"price"`
	Sum        int32      `json:"sum"`
	Quantity   float32    `json:"quantity"`
	Name       string     `json:"name"`
	Categories []Category `json:"categories"`
}

type Receipt struct {
	Id                   primitive.ObjectID `bson:"_id,omitempty" json:"id"`
	DateTime             string             `json:"dateTime"`
	TotalSum             int32              `json:"totalSum"`
	RetailPlaceAddress   string             `json:"retailPlaceAddress"`
	UserInn              string             `json:"userInn"`
	Items                []Purchase         `json:"items"`
	RawData              string             `json:"rawData"`
	Operator             string             `json:"operator"`
	Nds18                int32              `json:"nds18"`
	Nds10                int32              `json:"nds10"`
	User                 string             `json:"user"`
	CashTotalSum         int32              `json:"cashTotalSum"`
	EcashTotalSum        int32              `json:"ecashTotalSum"`
	FiscalSign           int64              `json:"fiscalSign"`
	FiscalDocumentNumber int64              `json:"fiscalDocumentNumber"`
	Owner                primitive.ObjectID `json:"owner" bson:"owner"`
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
