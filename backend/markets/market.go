package markets

import (
	"go.mongodb.org/mongo-driver/bson/primitive"
)

//Market is market.
type Market struct {
	Id   primitive.ObjectID `json:"id" bson:"_id,omitempty"`
	Name string             `json:"name"`
	Inns []string           `json:"inns"`
	Type MarketType         `json:"type"`
}

//MarketType is market type.
type MarketType string

const (
	Supermarket MarketType = "super_market"
	Fuel        MarketType = "fuel"
)
