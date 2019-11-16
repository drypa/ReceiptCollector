package nalogru_client

import (
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
	response, err := sendRequest(ofdsUrl, client, nalogruClient.Login, nalogruClient.Password)
	if err != nil {
		return err
	}
	bytes, err := ioutil.ReadAll(response.Body)
	if err != nil {
		return err
	}
	//406
	log.Printf("ODFS request status: %d and body: %s \n", response.StatusCode, string(bytes))
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
	response, err := sendRequest(kktsUrl, client, nalogruClient.Login, nalogruClient.Password)
	if err == nil && response.StatusCode == 200 {
		return ioutil.ReadAll(response.Body)
	}
	log.Println(err)
	if response != nil {
		log.Println(response.StatusCode)
	}
	return nil, err

}

func sendRequest(url string, client *http.Client, login string, password string) (*http.Response, error) {
	request, _ := http.NewRequest("GET", url, nil)
	addHeaders(request, login, password)
	return client.Do(request)
}

func addHeaders(request *http.Request, login string, password string) {
	request.SetBasicAuth(login, password)
	request.Header.Add("Device-OS", "Android 5.1")
	request.Header.Add("Version", "2")
	request.Header.Add("ClientVersion", "1.4.4.4")
	request.Header.Add("Device-Id", "123456")
}
