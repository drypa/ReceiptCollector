package nalogru

import (
	"go.mongodb.org/mongo-driver/bson/primitive"
	"log"
	"receipt_collector/nalogru/device"
	"testing"
)

var baseAddress = "https://irkkt-mobile.nalog.ru:8888"
var sessionId = "INSERT SESSION ID HERE"
var deviceId = primitive.NewObjectID().Hex()

func IgnoreTestClient_GetTicketId(t *testing.T) {
	d, err := createDevice(t, "", "")
	if err != nil {
		log.Println(err)
		t.Fail()
		return
	}
	client := NewClient(baseAddress, d)
	queryString := "INSERT BARCODE TEST HERE"

	id, err := client.GetTicketId(queryString)

	if err != nil {
		log.Println(err)
		t.Fail()
		return
	}
	if id == "" {
		log.Println("Got empty id")
		t.Fail()
		return
	}
	log.Println(id)

}

func createDevice(t *testing.T, secret string, token string) (*device.Device, error) {
	id, err := primitive.ObjectIDFromHex(deviceId)
	if err != nil {
		return nil, err
	}
	d := &device.Device{
		SessionId:    sessionId,
		Id:           id,
		ClientSecret: secret,
		RefreshToken: token,
	}
	return d, err
}

func IgnoreTestClient_GetTicketById(t *testing.T) {
	d, err := createDevice(t, "", "")
	if err != nil {
		log.Println(err)
		t.Fail()
		return
	}
	client := NewClient(baseAddress, d)

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
	d, err := createDevice(t, secret, refreshToken)
	if err != nil {
		log.Println(err)
		t.Fail()
		return
	}
	client := NewClient(baseAddress, d)

	d, err = client.RefreshSession()

	if err != nil {
		log.Println(err)
		t.Fail()
	}

	if d.SessionId == sessionId {
		log.Println("Session was not refreshed")
		t.Fail()
	}
	if d.RefreshToken == "" {
		log.Println("Refresh token is empty")
		t.Fail()
	}

}

func IgnoreTestClient_CheckReceiptExist(t *testing.T) {
	queryString := "INSERT VALID QUERY STRING HERE"
	d, err := createDevice(t, "", "")
	if err != nil {
		log.Println(err)
		t.Fail()
		return
	}
	client := NewClient(baseAddress, d)
	exist, err := client.CheckReceiptExist(queryString)

	if err != nil {
		log.Println(err)
		t.Fail()
		return
	}

	if exist == false {
		log.Println("bar code is invalid")
		t.Fail()
		return
	}

}
