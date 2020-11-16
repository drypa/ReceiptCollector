package nalogru

import (
	"receipt_collector/nalogru/qr"
	"testing"
	"time"
)

func TestOfdsUrlBuilder(t *testing.T) {
	baseAddress := "https://example.com"
	parsedReceipt := qr.Query{
		FiscalSign: "4245472848",
		Fd:         "9251440300012362",
		Fp:         "30813",
		Sum:        "515.00",
		N:          "1",
		Time:       time.Date(2019, time.Month(4), 23, 17, 47, 0, 0, time.Local),
	}
	actual := buildOfdsUrl(baseAddress, parsedReceipt)
	expected := "https://example.com/v1/ofds/*/inns/*/fss/9251440300012362/operations/1/tickets/30813?fiscalSign=4245472848&date=2019-04-23T17:47:00&sum=515.00"
	if actual != expected {
		t.Errorf("expected: %s but actual: %s", expected, actual)
	}
}
