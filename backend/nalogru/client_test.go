package nalogru

import (
	"log"
	"testing"
)

func TestClient_GetTicketId(t *testing.T) {
	baseAddress := "https://irkkt-mobile.nalog.ru:8888"
	sessionId := "5fb27661d47a9ac25731773a:3f2f9d45-c736-4fce-b390-f22f8570156a"
	deviceId := "c21NX6WnGtQ"
	client := NewClient(baseAddress, "", sessionId, "", deviceId)
	queryString := "t=20201104T1448&s=387.01&fn=9280440300804942&i=46469&fp=1158670860&n=1"

	id, err := client.GetTicketId(queryString)

	if err != nil {
		t.Fail()
	}
	if id == "" {
		t.Fail()
	}
	log.Println(id) //5fb2ad0e77016167fb9f7e21

}

func TestClient_GetTicketById(t *testing.T) {
	baseAddress := "https://irkkt-mobile.nalog.ru:8888"
	sessionId := "5fb27661d47a9ac25731773a:3f2f9d45-c736-4fce-b390-f22f8570156a"
	deviceId := "c21NX6WnGtQ"
	client := NewClient(baseAddress, "", sessionId, "", deviceId)

	id, err := client.GetTicketById("5fb2b7ba77016167fb9f8ca8")

	if err != nil {
		t.Fail()
	}
	if id == "" {
		t.Fail()
	}
	log.Println(id) //5fb2ad0e77016167fb9f7e21

}
