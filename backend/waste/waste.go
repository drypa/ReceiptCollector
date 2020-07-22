package waste

import (
	"time"
)

type Category string

type Waste struct {
	// Waste date.
	Date time.Time `bson:"date" json:"date"`
	// ReceiptId if exists.
	ReceiptId string `bson:"receipt_id" json:"receipt_id"`
	// Waste place.
	Place *Place `bson:"place" json:"place"`
	// Total spend sum.
	Sum float32 `bson:"sum" json:"sum"`
	// Users description.
	Description string `bson:"description json:"description"`
	// User owner.
	OwnerId string `bson:"owner_id" json:"owner_id"`
	// Waste category. Defined by market type.
	Category *Category `bson:"category" json:"category"`
	// Waste category from Category(id defined) or set manually.
	CategoryName string `bson:"category_name" json:"category_name"`
}

type Query struct {
	Sum float32
}

func MapByReceipt(wastes []Waste) map[string]*Waste {
	count := len(wastes)
	result := make(map[string]*Waste)
	for i := 0; i < count; i++ {
		el := wastes[i]
		result[el.ReceiptId] = &el
	}
	return result
}
