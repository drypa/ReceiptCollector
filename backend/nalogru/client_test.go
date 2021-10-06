package nalogru

import (
	"go.mongodb.org/mongo-driver/bson/primitive"
	"log"
	"net/http"
	"net/http/httptest"
	"receipt_collector/nalogru/device"
	"testing"
)

var baseAddress = "https://irkkt-mobile.nalog.ru:8888"
var sessionId = "INSERT SESSION ID HERE"
var deviceId = primitive.NewObjectID().Hex()

func IgnoreTestClient_GetTicketId(t *testing.T) {
	d, err := createDevice("", "")
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

func createDevice(secret string, token string) (*device.Device, error) {
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
	d, err := createDevice("", "")
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
	d, err := createDevice(secret, refreshToken)
	if err != nil {
		log.Println(err)
		t.Fail()
		return
	}
	client := NewClient(baseAddress, d)

	err = client.RefreshSession()

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
	log.Printf("SessionId: %s\n", d.SessionId)
	log.Printf("RefreshToken: %s\n", d.RefreshToken)

}

func IgnoreTestClient_CheckReceiptExist(t *testing.T) {
	queryString := "INSERT VALID QUERY STRING HERE"
	d, err := createDevice("", "")
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

func IgnoreTestHttpClient_NoError(t *testing.T) {
	svr := createServer(t)
	defer svr.Close()
	client := http.Client{}
	iterationErrorOccurred := -1
	for i := 0; i < 30_000; i++ {
		err := callServerWithCloseBody(client, svr)
		if err != nil {
			iterationErrorOccurred = i
		}
	}
	if iterationErrorOccurred == -1 {
		t.FailNow()
	}
}
func IgnoreTestHttpClient_Error(t *testing.T) {
	svr := createServer(t)
	defer svr.Close()
	client := http.Client{}
	for i := 0; i < 30_000; i++ {
		err := callServerWithoutCloseBody(client, svr)
		if err != nil {
			log.Println(i)
			log.Println(err.Error())
			t.FailNow()
		}
	}
}

func createServer(t *testing.T) *httptest.Server {
	return httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, err := w.Write([]byte("Hello"))
		if err != nil {
			log.Printf("Server fail: %v\n", err.Error())
			t.Fail()
		}
	}))
}

func callServerWithCloseBody(client http.Client, svr *httptest.Server) error {
	m, err := client.Get(svr.URL)
	defer m.Body.Close()
	return err

}
func callServerWithoutCloseBody(client http.Client, svr *httptest.Server) error {
	_, err := client.Get(svr.URL)
	return err

}
