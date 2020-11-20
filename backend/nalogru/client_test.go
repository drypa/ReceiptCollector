package nalogru

import (
	"log"
	"testing"
)

var baseAddress = "https://irkkt-mobile.nalog.ru:8888"
var sessionId = "INSERT SESSION ID HERE"
var deviceId = "12345"

func IgnoreTestClient_GetTicketId(t *testing.T) {
	sessionId := "INSERT SESSION ID HERE"
	deviceId := "12345"
	client := NewClient(baseAddress, "", sessionId, "", deviceId)
	queryString := "INSERT BARCODE TEST HERE"

	id, err := client.GetTicketId(queryString)

	if err != nil {
		log.Println(err)
		t.Fail()
	}
	if id == "" {
		log.Println("Got empty id")
		t.Fail()
	}
	log.Println(id)

}

func IgnoreTestClient_GetTicketById(t *testing.T) {
	client := NewClient(baseAddress, "", sessionId, "", deviceId)

	ticketId := "INSERT TICKET ID HERE"
	details, err := client.GetTicketById(ticketId)

	if err != nil {
		log.Println(err)
		t.Fail()
	}
	if details == nil {
		log.Println("Got nil result")
		t.Fail()
	}
}
