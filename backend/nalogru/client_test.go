package nalogru

import (
	"go.mongodb.org/mongo-driver/bson/primitive"
	"log"
	"testing"
)

var baseAddress = "https://irkkt-mobile.nalog.ru:8888"
var sessionId = "INSERT SESSION ID HERE"
var deviceId = primitive.NewObjectID().Hex()

type TestDevice struct {
	ClientSecret string
	SessionId    string
	RefreshToken string
	Id           string
}

func newTestDevice(secret string, token string) TestDevice {
	d := TestDevice{
		SessionId:    sessionId,
		Id:           deviceId,
		ClientSecret: secret,
		RefreshToken: token,
	}
	return d
}

func (d TestDevice) Refresh(newToken string, newSession string) {
	d.SessionId = newSession
	d.RefreshToken = newToken
}

func (d TestDevice) GetId() string {
	return d.Id
}

func (d TestDevice) GetSecret() string {
	return d.ClientSecret
}

func (d TestDevice) GetSessionId() string {
	return d.SessionId
}

func (d TestDevice) GetRefreshToken() string {
	return d.RefreshToken
}

func IgnoreTestClient_GetTicketId(t *testing.T) {
	d := newTestDevice("", "")

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

func IgnoreTestClient_GetTicketById(t *testing.T) {
	d := newTestDevice("", "")

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
	d := newTestDevice(secret, refreshToken)
	client := NewClient(baseAddress, d)

	apiClient, err := client.RefreshSession()

	if err != nil {
		log.Println(err)
		t.Fail()
	}

	if d.SessionId == apiClient.GetSessionId() {
		log.Println("Session was not refreshed")
		t.Fail()
	}
	if apiClient.GetRefreshToken() == "" {
		log.Println("Refresh token is empty")
		t.Fail()
	}

}

func IgnoreTestClient_CheckReceiptExist(t *testing.T) {
	queryString := "INSERT VALID QUERY STRING HERE"
	d := newTestDevice("", "")
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
