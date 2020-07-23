package waste

import "receipt_collector/markets"

type Place struct {
	Market      *markets.Market
	Name        string `bson:"name" json:"name"`
	Description string `bson:"description" json:"description"`
}
