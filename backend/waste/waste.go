package waste

import (
	"go.mongodb.org/mongo-driver/bson/primitive"
	"receipt_collector/receipts"
	"time"
)

type Category string

type Waste struct {
	// Waste date.
	Date time.Time
	// Receipt if exists.
	Receipt *receipts.UsersReceipt
	// Waste place.
	Place *Place
	// Total spend sum.
	Sum float32
	// Users description.
	Description string
	// User owner.
	Owner primitive.ObjectID
	// Waste category. Defined by market type.
	Category *Category
	// Waste category from Category(id defined) or set manually.
	CategoryName string
}

type Query struct {
	Sum float32
}
