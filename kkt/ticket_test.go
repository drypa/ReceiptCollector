package kkt

import (
	"encoding/json"
	"io/ioutil"
	"testing"
)

func IgnoreTest_ParseResponse(t *testing.T) {
	bytes, err := ioutil.ReadFile("PATH-TO-RESPONSE.json")

	if err != nil {
		t.Fail()
	}

	details := &TicketDetails{}
	err = json.Unmarshal(bytes, details)
	if err != nil {
		t.Fail()
	}
}

func Test_buildCheckReceiptUrl(t *testing.T) {
	url, err := buildCheckReceiptUrl("www.ru", "t=20200102T1617&s=141.56&fn=7882000100181230&i=12345&fp=9876543210&n=1")

	if err != nil {

		t.Fail()
	}
	const expected = "www.ru/v2/check/ticket?fsId=7882000100181230&operationType=1&documentId=12345&fiscalSign=9876543210&date=2020-01-02T16%3A17%3A00&sum=14156"
	if url != expected {
		t.Logf("Expected %s but got %s", expected, url)
		t.Fail()
	}

}
