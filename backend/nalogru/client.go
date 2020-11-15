package nalogru

import (
	"errors"
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

func (nalogruClient Client) SendOdfsRequest(queryString string) error {
	parseResult, err := Parse(queryString)
	if err != nil {
		return err
	}
	ofdsUrl := buildOfdsUrl(nalogruClient.BaseAddress, parseResult)
	client := &http.Client{}
	request, err := createRequest(ofdsUrl)

	if err != nil {
		return err
	}
	response, err := sendRequest(request, client)
	if err != nil {
		return err
	}
	if response.StatusCode != http.StatusOK && response.StatusCode != http.StatusAccepted {
		//406
		log.Printf("ODFS request status: %d \n", response.StatusCode)
		return errors.New(response.Status)
	}
	return nil
}

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

func (nalogruClient Client) SendKktsRequest(queryString string) ([]byte, error) {
	client := &http.Client{}
	parseResult, err := Parse(queryString)
	if err != nil {
		return nil, err
	}

	kktsUrl := BuildKktsUrl(nalogruClient.BaseAddress, parseResult)
	log.Printf("Kkt URL: %s\n", kktsUrl)
	request, err := createRequest(kktsUrl)
	if err != nil {
		return nil, err
	}
	addAuth(request, nalogruClient.SessionId, nalogruClient.DeviceId)
	response, err := sendRequest(request, client)

	if err != nil {
		log.Printf("KKTs request error %v.", err)
		return nil, err
	}

	if response.StatusCode == http.StatusAccepted {
		return nil, errors.New(NotReadyYet)
	}

	all, err := ioutil.ReadAll(response.Body)
	if response.StatusCode == http.StatusOK {
		return all, err
	}
	log.Println(response.StatusCode)
	return nil, errors.New(string(all))
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
