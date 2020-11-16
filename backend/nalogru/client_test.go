package nalogru

import (
	"log"
	"testing"
)

func IgnorTestClient_GetTicketId(t *testing.T) {
	baseAddress := "https://irkkt-mobile.nalog.ru:8888"
	sessionId := "INSERT SESSION ID HERE"
	deviceId := "12345"
	client := NewClient(baseAddress, "", sessionId, "", deviceId)
	queryString := "INSERT BARCODE TEST HERE"

	id, err := client.GetTicketId(queryString)

	if err != nil {
		t.Fail()
	}
	if id == "" {
		t.Fail()
	}
	log.Println(id)

}
