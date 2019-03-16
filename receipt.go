package main

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
}
