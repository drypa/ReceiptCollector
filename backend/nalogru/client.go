package nalogru

import (
	"bytes"
	"compress/gzip"
	"encoding/json"
	"errors"
	"io"
	"io/ioutil"
	"log"
	"net/http"
	"receipt_collector/dispose"
	"receipt_collector/nalogru/device"
)

type Client struct {
	BaseAddress string
	device      device.ApiClient
}

var AuthError = errors.New("auth failed")
var InternalError = errors.New("internal failed")

//NewClient - creates instance of Client.
func NewClient(baseAddress string, device device.ApiClient) *Client {
	return &Client{
		BaseAddress: baseAddress,
		device:      device,
	}
}

const (
	DailyLimitReached = "too_many_requests"
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

	request, err := http.NewRequest(http.MethodGet, url, nil)
	if err != nil {
		log.Printf("Could't not create request for %s", url)
		return false, err
	}
	addHeaders(request, nalogruClient.device.GetId())
	resp, err := client.Do(request)
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

	req, err := json.Marshal(payload)
	if err != nil {
		return "", err
	}
	reader := bytes.NewReader(req)
	url := nalogruClient.BaseAddress + "/v2/ticket"
	request, err := http.NewRequest(http.MethodPost, url, reader)
	addHeaders(request, nalogruClient.device.GetId())
	addAuth(request, nalogruClient.device.GetSessionId())
	res, err := sendRequest(request, client)
	if err != nil {
		log.Printf("Can't POST %s\n", url)
		return "", err
	}

	body, err := readBody(res)
	if err != nil {
		return "", err
	}

	if res.StatusCode == http.StatusTooManyRequests {
		log.Printf("Too Many Requests : %d\n", res.StatusCode)
		return "", errors.New(DailyLimitReached)
	}

	if res.StatusCode != http.StatusOK {
		log.Printf("Get ticket id error: %d\n", res.StatusCode)
		file, err := ioutil.TempFile("/var/lib/receipts/error/", "*.err")
		if err != nil {
			log.Println("failed to create error response file")
			return "", err
		}
		defer dispose.Dispose(file.Close, "failed to close error file.")
		_, err = file.Write(body)
		if err != nil {
			log.Println("failed to write response to file")
			return "", err
		}

		if res.StatusCode == http.StatusUnauthorized {
			err = AuthError
		} else {
			err = InternalError
		}

		return "", err
	}

	ticketIdResp := &TicketIdResponse{}
	err = json.Unmarshal(body, ticketIdResp)
	if err != nil {
		log.Println("Can't unmarshal response")
		return "", err
	}
	return ticketIdResp.Id, nil
}

func readBody(res *http.Response) ([]byte, error) {
	defer dispose.Dispose(res.Body.Close, "failed to close response body.")
	var bodyReader io.ReadCloser
	var err error
	switch res.Header.Get("Content-Encoding") {
	case "gzip":
		bodyReader, err = gzip.NewReader(res.Body)
		if err != nil {
			log.Printf("Can't create gzip reader \n")
			return nil, err
		}
		defer dispose.Dispose(bodyReader.Close, "failed to close gzip reader.")
	default:
		bodyReader = res.Body
	}

	return ioutil.ReadAll(bodyReader)
}

//GetTicketById get ticket by id from nalog.ru api.
func (nalogruClient *Client) GetTicketById(id string) (*TicketDetails, error) {
	client := &http.Client{}

	url := nalogruClient.BaseAddress + "/v2/tickets/" + id
	request, err := http.NewRequest(http.MethodGet, url, nil)
	addHeaders(request, nalogruClient.device.GetId())
	addAuth(request, nalogruClient.device.GetSessionId())
	res, err := sendRequest(request, client)
	if err != nil {
		log.Printf("Can't GET %s\n", url)
		return nil, err
	}

	all, err := readBody(res)
	if err != nil {
		log.Printf("failed to read response body. status code %d\n", res.StatusCode)
		return nil, err
	}

	if res.StatusCode == http.StatusUnauthorized {
		return nil, AuthError
	}

	if res.StatusCode != http.StatusOK {
		log.Printf("GET receipt error: %d\n", res.StatusCode)
		err = ioutil.WriteFile("/var/lib/receipts/error/"+id+".json", all, 0644)
		return nil, err
	}

	details := &TicketDetails{}

	err = ioutil.WriteFile("/var/lib/receipts/raw/"+id+".json", all, 0644)

	err = json.Unmarshal(all, details)
	if err != nil {
		log.Println("Can't decode response body")

		return nil, err
	}

	return details, nil
}

type RefreshRequest struct {
	ClientSecret string `json:"client_secret"`
	RefreshToken string `json:"refresh_token"`
}

type RefreshResponse struct {
	SessionId    string `json:"sessionId"`
	RefreshToken string `json:"refresh_token"`
}

func (nalogruClient *Client) RefreshSession() (device.ApiClient, error) {
	client := &http.Client{}

	payload := RefreshRequest{
		ClientSecret: nalogruClient.device.GetSecret(),
		RefreshToken: nalogruClient.device.GetRefreshToken(),
	}

	resp, err := json.Marshal(payload)
	if err != nil {
		return nil, err
	}
	reader := bytes.NewReader(resp)

	url := nalogruClient.BaseAddress + "/v2/mobile/users/refresh"
	request, err := http.NewRequest(http.MethodPost, url, reader)
	addHeaders(request, nalogruClient.device.GetId())
	res, err := sendRequest(request, client)
	if err != nil {
		log.Printf("Can't POST %s\n", url)
		return nil, err
	}
	if res.StatusCode != http.StatusOK {
		log.Printf("Refresh session error: %d\n", res.StatusCode)
		return nil, err
	}

	response := &RefreshResponse{}
	err = json.NewDecoder(res.Body).Decode(response)
	if err != nil {
		log.Println("Can't decode response body")
		return nil, err
	}
	log.Printf("%+v\n", response)
	nalogruClient.device.Refresh(response.RefreshToken, response.SessionId)
	return nalogruClient.device, nil
}

func sendRequest(request *http.Request, client *http.Client) (*http.Response, error) {
	return client.Do(request)
}

func addAuth(request *http.Request, sessionId string) {
	request.Header.Add("sessionId", sessionId)
}

func addHeaders(request *http.Request, deviceId string) {
	request.Header.Add("ClientVersion", "2.13.0")
	request.Header.Add("Device-Id", deviceId)
	request.Header.Add("Device-OS", "Android")
	request.Header.Add("Connection", "Keep-Alive")
	request.Header.Add("Accept-Encoding", "gzip")
	request.Header.Add("User-Agent", "okhttp/4.9.0")
	request.Header.Add("Content-Type", "application/json; charset=utf-8")
}
