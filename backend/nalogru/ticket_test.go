package nalogru

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
