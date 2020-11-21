package nalogru

import (
	"log"
	"testing"
)

var baseAddress = "https://irkkt-mobile.nalog.ru:8888"
var sessionId = "INSERT SESSION ID HERE"
var deviceId = "12345"

func IgnoreTestClient_GetTicketId(t *testing.T) {
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
	log.Printf("%+v\n", details)
}

func IgnoreTestClient_RefreshSession(t *testing.T) {
	secret := "PASS CLIENT SECRET HERE"
	refreshToken := "PASS REFRESH TOKEN HERE"
	client := NewClient(baseAddress, secret, sessionId, refreshToken, deviceId)

	err := client.RefreshSession()

	if err != nil {
		log.Println(err)
		t.Fail()
	}

	if client.SessionId == sessionId {
		log.Println("Session was not refreshed")
		t.Fail()
	}
	if client.RefreshToken == "" {
		log.Println("Refresh token is empty")
		t.Fail()
	}

}
