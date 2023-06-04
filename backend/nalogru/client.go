package nalogru

import (
	"bytes"
	"compress/gzip"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"receipt_collector/dispose"
	"receipt_collector/nalogru/device"
	"time"
)

type Client struct {
	BaseAddress string
	device      *device.Device
}

var AuthError = errors.New("auth failed")
var InternalError = errors.New("internal failed")

// NewClient - creates instance of Client.
func NewClient(baseAddress string, device *device.Device) *Client {
	return &Client{
		BaseAddress: baseAddress,
		device:      device,
	}
}

const (
	DailyLimitReached = "too_many_requests"
)

// CheckReceiptExist send request to check receipt exist in Nalog.ru api.
func (nalogruClient Client) CheckReceiptExist(queryString string) (bool, error) {
	client := createHttpClient()
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
	addHeaders(request, nalogruClient.device.Id.Hex())
	resp, err := client.Do(request)

	if err != nil {
		log.Printf("Could't check receipt %s", url)
		return false, err
	}
	defer resp.Body.Close()
	if resp.StatusCode == http.StatusOK || resp.StatusCode == http.StatusNoContent {
		log.Println("Check passed")
		return true, nil
	}
	log.Printf("Receipt is invalid? %s", url)
	return false, nil
}

func createHttpClient() *http.Client {
	return &http.Client{Timeout: time.Second * 10}
}

// TicketIdRequest is request object to get Ticket id.
type TicketIdRequest struct {
	Qr          string      `json:"qr"`
	Coordinates interface{} `json:"coordinates"`
}

// TicketIdResponse - response on TicketIdRequest.
type TicketIdResponse struct {
	Kind   string `json:"kind"`
	Id     string `json:"id"`
	Status int    `json:"status"`
}

// GetTicketId - send ticket id request to nalog.ru API.
func (nalogruClient *Client) GetTicketId(queryString string) (string, error) {
	client := createHttpClient()
	payload := TicketIdRequest{Qr: queryString}

	req, err := json.Marshal(payload)
	if err != nil {
		return "", err
	}
	reader := bytes.NewReader(req)
	url := nalogruClient.BaseAddress + "/v2/ticket"
	request, err := http.NewRequest(http.MethodPost, url, reader)
	addHeaders(request, nalogruClient.device.Id.Hex())
	addAuth(request, nalogruClient.device.SessionId)
	res, err := sendRequest(request, client)

	if err != nil {
		log.Printf("Can't POST %s\n", url)
		return "", err
	}
	defer res.Body.Close()

	body, err := readBody(res)
	if err != nil {
		return "", err
	}

	if res.StatusCode == http.StatusTooManyRequests {
		log.Printf("Too Many Requests : %d\n", res.StatusCode)
		return "", errors.New(DailyLimitReached)
	}

	if res.StatusCode != http.StatusOK {
		log.Printf("Get ticket id(%s) error: %d\n", queryString, res.StatusCode)
		file, err := os.CreateTemp("/var/lib/receipts/error/", "*.err")
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

func (nalogruClient *Client) GetDevice() device.Device {
	return device.Device{
		ClientSecret: nalogruClient.device.ClientSecret,
		SessionId:    nalogruClient.device.SessionId,
		RefreshToken: nalogruClient.device.RefreshToken,
		Id:           nalogruClient.device.Id,
	}
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

	return io.ReadAll(bodyReader)
}

// GetTicketById get ticket by id from nalog.ru api.
func (nalogruClient *Client) GetTicketById(id string) (*TicketDetails, error) {
	client := createHttpClient()

	url := nalogruClient.BaseAddress + "/v2/tickets/" + id
	request, err := http.NewRequest(http.MethodGet, url, nil)
	addHeaders(request, nalogruClient.device.Id.Hex())
	addAuth(request, nalogruClient.device.SessionId)
	res, err := sendRequest(request, client)

	if err != nil {
		log.Printf("Can't GET %s\n", url)
		return nil, err
	}
	defer res.Body.Close()

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
		err = os.WriteFile("/var/lib/receipts/error/"+id+".json", all, 0644)
		return nil, err
	}

	details := &TicketDetails{}

	err = os.WriteFile("/var/lib/receipts/raw/"+id+".json", all, 0644)

	err = json.Unmarshal(all, details)
	if err != nil {
		log.Println("Can't decode response body")

		return nil, err
	}

	return details, nil
}

// GetElectronicTickets request all electronic receipts added by email or phone from nalog.ru api.
func (nalogruClient *Client) GetElectronicTickets(device *device.Device) ([]*TicketDetails, error) {
	client := createHttpClient()

	url := nalogruClient.BaseAddress + "/v2/tickets-with-electro"
	request, err := http.NewRequest(http.MethodGet, url, nil)

	addHeaders(request, device.Id.Hex())
	addAuth(request, device.SessionId)
	res, err := sendRequest(request, client)

	if err != nil {
		log.Printf("Can't GET %s\n", url)
		return nil, err
	}
	defer res.Body.Close()

	if res.StatusCode == http.StatusUnauthorized {
		return nil, AuthError
	}

	if res.StatusCode != http.StatusOK {
		log.Printf("GET electronic receipts error: %d\n", res.StatusCode)
		return nil, err
	}
	all, err := readBody(res)
	if err != nil {
		log.Printf("failed to read response body. status code %d\n", res.StatusCode)
		return nil, err
	}
	err = os.WriteFile("/var/lib/receipts/electro/1.json", all, 0644)
	var tickets []*TicketDetails

	err = json.Unmarshal(all, &tickets)
	if err != nil {
		log.Println("Can't decode response body")

		return nil, err
	}

	return tickets, nil
}

type RefreshRequest struct {
	ClientSecret string `json:"client_secret"`
	RefreshToken string `json:"refresh_token"`
}

type RefreshResponse struct {
	SessionId    string `json:"sessionId"`
	RefreshToken string `json:"refresh_token"`
}

func (nalogruClient *Client) RefreshSession() error {
	client := createHttpClient()

	payload := RefreshRequest{
		ClientSecret: nalogruClient.device.ClientSecret,
		RefreshToken: nalogruClient.device.RefreshToken,
	}

	resp, err := json.Marshal(payload)
	if err != nil {
		return err
	}
	reader := bytes.NewReader(resp)

	url := nalogruClient.BaseAddress + "/v2/mobile/users/refresh"
	request, err := http.NewRequest(http.MethodPost, url, reader)
	addHeaders(request, nalogruClient.device.Id.Hex())
	res, err := sendRequest(request, client)

	if err != nil {
		log.Printf("Can't POST %s\n", url)
		return err
	}
	defer res.Body.Close()

	if res.StatusCode != http.StatusOK {
		log.Printf("Refresh session error: %d\n", res.StatusCode)
		return errors.New(fmt.Sprintf("HTTP error: %d", res.StatusCode))
	}

	response := &RefreshResponse{}
	err = json.NewDecoder(res.Body).Decode(response)
	if err != nil {
		log.Println("Can't decode response body")
		return err
	}
	log.Printf("%+v\n", response)
	nalogruClient.device.RefreshToken = response.RefreshToken
	nalogruClient.device.SessionId = response.SessionId
	return nil
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
