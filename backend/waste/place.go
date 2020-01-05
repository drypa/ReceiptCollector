package waste

import "receipt_collector/markets"

type Place struct {
	Market      *markets.Market
	Name        string
	Description string
}
