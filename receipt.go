package main

import "fmt"

type Purchase struct {
	Price    int32
	Sum      int32
	Quantity float32
	Name     string
}

type Receipt struct {
	DateTime           string
	TotalSum           int32
	RetailPlaceAddress string
	UserInn            string
	Items              []Purchase
	RawData            string
	Operator           string
	Nds18              int32
	nds10              int32
	User               string
	CashTotalSum       int32
	EcashTotalSum      int32
	FiscalSign         int64
}

func (purchase *Purchase) String() string {
	return fmt.Sprintf("Purchase: Name=%s; Price=%d; Quantity=%f; Sum=%d", purchase.Name, purchase.Price, purchase.Quantity, purchase.Sum)
}

func (receipt *Receipt) String() string {
	return fmt.Sprintf("Receipt: Date=%s; RetailAddress=%s; Inn=%s; ItemsCount=%d", receipt.DateTime, receipt.RetailPlaceAddress, receipt.UserInn, len(receipt.Items))
}
