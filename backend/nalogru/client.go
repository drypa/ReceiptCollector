package nalogru

import (
	"errors"
	"io/ioutil"
	"log"
	"net/http"
)

type Client struct {
	BaseAddress string
	Login       string
	Password    string
}

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
	if response.StatusCode != http.StatusOK || response.StatusCode != http.StatusAccepted {
		//406
		log.Printf("ODFS request status: %d \n", response.StatusCode)
		return errors.New(response.Status)
	}
	return nil
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
	addAuth(request, nalogruClient.Login, nalogruClient.Password)
	response, err := sendRequest(request, client)

	if err != nil {
		log.Printf("KKTs request error %v.", err)
		return nil, err
	}

	if response.StatusCode == http.StatusOK {
		return ioutil.ReadAll(response.Body)
	}
	if response != nil {
		log.Println(response.StatusCode)
		bytes, _ := ioutil.ReadAll(response.Body)
		log.Println(string(bytes))
	}
	return nil, err

}

func sendRequest(request *http.Request, client *http.Client) (*http.Response, error) {
	return client.Do(request)
}

func createRequest(url string) (*http.Request, error) {
	request, _ := http.NewRequest("GET", url, nil)
	addHeaders(request)
	return request, nil
}

func addAuth(request *http.Request, login string, password string) {
	request.SetBasicAuth(login, password)
}

func addHeaders(request *http.Request) {
	request.Header.Add("Device-OS", "Adnroid 6.0.1") //not my misspell. is is from traffic dump
	request.Header.Add("Version", "2")
	request.Header.Add("ClientVersion", "1.4.4.4")
	request.Header.Add("Device-Id", "123456")
	request.Header.Add("Connection", "Keep-Alive")
	request.Header.Add("User-Agent", "okhttp/3.0.1")
}
