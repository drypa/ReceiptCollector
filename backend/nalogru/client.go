package nalogru

import (
	"bytes"
	"encoding/json"
	"io/ioutil"
	"log"
	"net/http"
)

type Client struct {
	BaseAddress  string
	ClientSecret string
	SessionId    string
	RefreshToken string
	DeviceId     string
}

//NewClient - creates instance of Client.
func NewClient(baseAddress string, clientSecret string, sessionId string, refreshToken string, deviceId string) *Client {
	return &Client{
		BaseAddress:  baseAddress,
		ClientSecret: clientSecret,
		SessionId:    sessionId,
		RefreshToken: refreshToken,
		DeviceId:     deviceId,
	}
}

const (
	TicketNotFound    string = "the ticket was not found"
	DailyLimitReached string = "daily limit reached for the specified user"
	NotReadyYet       string = "not ready yet"
)

//CheckReceiptExist send request to check receipt exist in Nalog.ru api.
func (nalogruClient Client) CheckReceiptExist(queryString string) (bool, error) {
	client := &http.Client{}
	url, err := buildCheckReceiptUrl(nalogruClient.BaseAddress, queryString)
	if err != nil {
		log.Printf("Could't build url for %s", queryString)
		return false, err
	}
	log.Printf("Check Request: %s", url)
	resp, err := client.Get(url)
	if err != nil {
		log.Printf("Could't check receipt %s", url)
		return false, err
	}
	if resp.StatusCode == http.StatusOK || resp.StatusCode == http.StatusNoContent {
		log.Println("Check passed")
		return true, nil
	}
	log.Printf("Receipt is invalid? %s", url)
	return false, nil
}

//TicketIdRequest is request object to get Ticket id.
type TicketIdRequest struct {
	Qr string `json:"qr"`
}

//TicketIdResponse - response on TicketIdRequest.
type TicketIdResponse struct {
	Kind   string `json:"kind"`
	Id     string `json:"id"`
	Status int    `json:"status"`
}

//GetTicketId - send ticket id request to nalog.ru API.
func (nalogruClient *Client) GetTicketId(queryString string) (string, error) {
	client := &http.Client{}
	payload := TicketIdRequest{Qr: queryString}

	resp, err := json.Marshal(payload)
	if err != nil {
		return "", err
	}
	reader := bytes.NewReader(resp)
	url := nalogruClient.BaseAddress + "/v2/ticket"
	request, err := http.NewRequest(http.MethodPost, url, reader)
	addHeaders(request)
	addAuth(request, nalogruClient.SessionId, nalogruClient.DeviceId)
	res, err := sendRequest(request, client)
	if err != nil {
		log.Printf("Can't POST %s\n", url)
		return "", err
	}
	if res.StatusCode != http.StatusOK {
		log.Printf("POST error: %d\n", res.StatusCode)
		return "", err
	}
	response, err := ioutil.ReadAll(res.Body)
	if err != nil {
		log.Println("Can't read http response body")
		return "", err
	}
	ticketIdResp := &TicketIdResponse{}
	err = json.Unmarshal(response, ticketIdResp)
	if err != nil {
		log.Println("Can't unmarshal response")
		return "", err
	}
	return ticketIdResp.Id, nil
}

func (nalogruClient *Client) GetTicketById(id string) (string, error) {
	client := &http.Client{}

	url := nalogruClient.BaseAddress + "/v2/tickets/" + id
	request, err := http.NewRequest(http.MethodGet, url, nil)
	addHeaders(request)
	addAuth(request, nalogruClient.SessionId, nalogruClient.DeviceId)
	res, err := sendRequest(request, client)
	if err != nil {
		log.Printf("Can't GET %s\n", url)
		return "", err
	}
	if res.StatusCode != http.StatusOK {
		log.Printf("GET receipt error: %d\n", res.StatusCode)
		return "", err
	}
	response, err := ioutil.ReadAll(res.Body)
	if err != nil {
		log.Println("Can't read http response body")
		return "", err
	}

	ioutil.WriteFile("/home/drypa/"+id+".receipt", response, 0644)
	return url, nil
}

func sendRequest(request *http.Request, client *http.Client) (*http.Response, error) {
	return client.Do(request)
}

func createRequest(url string) (*http.Request, error) {
	request, _ := http.NewRequest("GET", url, nil)
	addHeaders(request)
	return request, nil
}

func addAuth(request *http.Request, sessionId string, deviceId string) {
	request.Header.Add("Device-Id", deviceId)
	request.Header.Add("sessionId", sessionId)
}

func addHeaders(request *http.Request) {
	request.Header.Add("Device-OS", "Android")
	request.Header.Add("Version", "2")
	request.Header.Add("ClientVersion", "2.9.0")
	request.Header.Add("Connection", "Keep-Alive")
	request.Header.Add("User-Agent", "okhttp/4.2.2")
	request.Header.Add("Content-Type", "application/json; charset=utf-8")
}
