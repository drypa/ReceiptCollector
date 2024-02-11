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
	"net/http/httputil"
	"os"
	"receipt_collector/dispose"
	"receipt_collector/nalogru/device"
	"time"
)

type Client struct {
	BaseAddress string
}

var InternalError = errors.New("internal failed")

// NewClient - creates instance of Client.
func NewClient(baseAddress string) *Client {
	return &Client{
		BaseAddress: baseAddress,
	}
}

const (
	DailyLimitReached = "too_many_requests"
)

// CheckReceiptExist send request to check receipt exist in Nalog.ru api.
func (nalogruClient *Client) CheckReceiptExist(queryString string, device *device.Device) (bool, error) {
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
	addHeaders(request, device.Id.Hex())
	resp, err := client.Do(request)

	if err != nil {
		log.Printf("Could't check receipt %s", url)
		return false, err
	}
	defer dispose.Dispose(resp.Body.Close, "Can't close HTTP response body")
	if resp.StatusCode == http.StatusOK || resp.StatusCode == http.StatusNoContent {
		log.Println("Check passed")
		return true, nil
	}
	log.Printf("Receipt is invalid? %s", url)
	return false, nil
}

func createHttpClient() *http.Client {
	return &http.Client{Timeout: time.Minute}
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

type PhoneAuthRequest struct {
	ClientSecret string `json:"client_secret"`
	Os           string `json:"os"`
	Phone        string `json:"phone"`
}

// GetTicketId - send ticket id request to nalog.ru API.
func (nalogruClient *Client) GetTicketId(queryString string, device *device.Device) (string, error) {
	payload := TicketIdRequest{Qr: queryString}

	req, err := json.Marshal(payload)
	if err != nil {
		return "", err
	}
	reader := bytes.NewReader(req)
	url := nalogruClient.BaseAddress + "/v2/ticket"
	request, err := http.NewRequest(http.MethodPost, url, reader)
	res, err := nalogruClient.sendAuthenticatedRequest(request, device)

	if err != nil {
		log.Printf("Can't POST %s\n", url)
		return "", err
	}
	defer dispose.Dispose(res.Body.Close, "Can't close HTTP response body")

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

		err = InternalError

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

	return io.ReadAll(bodyReader)
}

// GetTicketById get ticket by id from nalog.ru api.
func (nalogruClient *Client) GetTicketById(id string, device *device.Device) (*TicketDetails, error) {

	url := nalogruClient.BaseAddress + "/v2/tickets/" + id
	request, err := http.NewRequest(http.MethodGet, url, nil)
	res, err := nalogruClient.sendAuthenticatedRequest(request, device)

	if err != nil {
		log.Printf("Can't GET %s\n", url)
		return nil, err
	}
	defer dispose.Dispose(res.Body.Close, "Can't close HTTP response body")

	all, err := readBody(res)
	if err != nil {
		log.Printf("failed to read response body. status code %d\n", res.StatusCode)
		return nil, err
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

	url := nalogruClient.BaseAddress + "/v2/tickets-with-electro"
	request, err := http.NewRequest(http.MethodGet, url, nil)

	res, err := nalogruClient.sendAuthenticatedRequest(request, device)

	if err != nil {
		log.Printf("Can't GET %s\n", url)
		return nil, err
	}
	defer dispose.Dispose(res.Body.Close, "Can't close response body")

	if res.StatusCode != http.StatusOK {
		log.Printf("GET electronic receipts error: %d\n", res.StatusCode)
		return nil, err
	}
	all, err := readBody(res)
	if err != nil {
		log.Printf("failed to read response body. status code %d error: %v\n", res.StatusCode, err)
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

func (nalogruClient *Client) AuthByPhone(device *device.Device) error {
	payload := PhoneAuthRequest{
		ClientSecret: device.ClientSecret,
		Os:           "android",
		Phone:        device.Phone,
	}

	req, err := json.Marshal(payload)
	if err != nil {
		log.Println("Unable to serialize request")
		return err
	}
	reader := bytes.NewReader(req)
	url := nalogruClient.BaseAddress + "/v2/auth/phone/request"
	request, err := http.NewRequest(http.MethodPost, url, reader)
	addHeaders(request, device.Id.Hex())
	client := createHttpClient()
	_, err = sendRequest(request, client)

	if err != nil {
		log.Printf("Can't POST %s\n", url)
		return err
	}

	return nil
}

func (nalogruClient *Client) sendAuthenticatedRequest(r *http.Request, device *device.Device) (*http.Response, error) {
	addHeaders(r, device.Id.Hex())
	addAuth(r, device.SessionId)
	client := createHttpClient()
	res, err := sendRequest(r, client)

	if err != nil {
		return nil, err
	}

	if res.StatusCode == http.StatusUnauthorized {
		err = nalogruClient.RefreshSession(device)
		if err != nil {
			log.Printf("failed to refresh session. %v\n", err)
			return nil, err
		}
		res, err = sendRequest(r, client)
	}
	return res, err
}

type RefreshRequest struct {
	ClientSecret string `json:"client_secret"`
	RefreshToken string `json:"refresh_token"`
}

type RefreshResponse struct {
	SessionId    string `json:"sessionId"`
	RefreshToken string `json:"refresh_token"`
}

type PhoneVerificationRequest struct {
	ClientSecret string `json:"client_secret"`
	Code         string `json:"code"`
	Phone        string `json:"phone"`
}
type PhoneVerificationResponse struct {
	SessionId    string `json:"sessionId"`
	RefreshToken string `json:"refresh_token"`
	Phone        string `json:"phone"`
	Name         string `json:"name"`
	Email        string `json:"email"`
}

// RefreshSession used to refresh session by RefreshToken.
func (nalogruClient *Client) RefreshSession(device *device.Device) error {
	client := createHttpClient()

	payload := RefreshRequest{
		ClientSecret: device.ClientSecret,
		RefreshToken: device.RefreshToken,
	}

	resp, err := json.Marshal(payload)
	if err != nil {
		return err
	}
	reader := bytes.NewReader(resp)

	url := nalogruClient.BaseAddress + "/v2/mobile/users/refresh"
	request, err := http.NewRequest(http.MethodPost, url, reader)
	addHeaders(request, device.Id.Hex())
	res, err := sendRequest(request, client)

	if err != nil {
		log.Printf("Can't POST %s\n", url)
		return err
	}
	defer dispose.Dispose(res.Body.Close, "Can't close HTTP response body")

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

	return device.Update(response.SessionId, response.RefreshToken)
}

func (nalogruClient *Client) VerifyPhone(device *device.Device, code string) error {
	client := createHttpClient()

	payload := PhoneVerificationRequest{
		ClientSecret: device.ClientSecret,
		Code:         code,
		Phone:        device.Phone,
	}

	resp, err := json.Marshal(payload)
	if err != nil {
		return err
	}
	reader := bytes.NewReader(resp)

	url := nalogruClient.BaseAddress + "/v2/auth/phone/verify"
	request, err := http.NewRequest(http.MethodPost, url, reader)
	addHeaders(request, device.Id.Hex())
	res, err := sendRequest(request, client)

	if err != nil {
		log.Printf("Can't POST %s\n", url)
		return err
	}
	defer dispose.Dispose(res.Body.Close, "Can't close HTTP response body")

	if res.StatusCode != http.StatusOK {
		log.Printf("Phone verification error: %d\n", res.StatusCode)
		return errors.New(fmt.Sprintf("HTTP error: %d", res.StatusCode))
	}

	response := &PhoneVerificationResponse{}
	err = json.NewDecoder(res.Body).Decode(response)
	if err != nil {
		log.Println("Can't decode response body")
		return err
	}
	log.Printf("%+v\n", response)
	err = device.Update(response.SessionId, response.RefreshToken)
	return err
}

func sendRequest(request *http.Request, client *http.Client) (*http.Response, error) {
	dump, err := httputil.DumpRequestOut(request, true)
	if err != nil {
		log.Fatal(err)
	}

	fmt.Printf("%q", dump)

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
