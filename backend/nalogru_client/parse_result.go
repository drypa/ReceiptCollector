package nalogru_client

import (
	"time"
)

type ParseResult struct {
	FiscalSign string
	Fd         string
	Fp         string
	Time       time.Time
	Sum        string
	N          string
}
