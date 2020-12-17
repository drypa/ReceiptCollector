package qr

import (
	"testing"
	"time"
)

var date = time.Date(2019, 4, 9, 13, 3, 0, 0, time.Local)

func TestParseQuery(t *testing.T) {

	testData := map[string]Query{
		"t=20190409T1303&s=333.00&fn=8710000101407564&i=91331&fp=865669710&n=1": {
			FiscalSign: "865669710",
			Fd:         "8710000101407564",
			Fp:         "91331",
			Time:       date,
			Sum:        "333.00",
			N:          "1",
		},
	}
	for key, val := range testData {
		query, err := Parse(key)
		if err != nil || query != val {
			t.Errorf("Parse was incorrect got %s, want %s", query, val)
		}
	}
}

func TestQuery_Validate(t *testing.T) {
	query := Query{
		FiscalSign: "865669710",
		Fd:         "8710000101407564",
		Fp:         "91331",
		Time:       date,
		Sum:        "333.00",
		N:          "1",
	}
	err := query.Validate()
	if err != nil {
		t.Errorf("Query validation error %v", err)
	}
}
