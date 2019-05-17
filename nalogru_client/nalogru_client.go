package nalogru_client

import (
	"fmt"
	"io/ioutil"
	"net/http"
	"time"
)

func GetRawReceipt(baseAddress string, receiptParams ParseResult, login string, password string) ([]byte, error) {
	odfsUrl := buildOfdsUrl(baseAddress, receiptParams)
	fmt.Println(odfsUrl)
	kktUrl := BuildKktsUrl(baseAddress, receiptParams)
	fmt.Println(kktUrl)
	client := &http.Client{}
	sendOdfsRequest(odfsUrl, client, login, password)
	bytes, err := sendKktsRequest(kktUrl, client, login, password)
	return bytes, err
}

func sendOdfsRequest(url string, client *http.Client, login string, password string) {
	response, err := sendRequest(url, client, login, password)
	check(err)
	bytes, err := ioutil.ReadAll(response.Body)
	check(err)
	//406
	fmt.Printf("ODFS request status: %d and body: %s \n", response.StatusCode, string(bytes))
}

func sendKktsRequest(url string, client *http.Client, login string, password string) ([]byte, error) {
	retry := 0
	for {
		response, err := sendRequest(url, client, login, password)
		if err == nil && response.StatusCode == 200 {
			return ioutil.ReadAll(response.Body)
		}
		fmt.Println(err)
		if response != nil {
			fmt.Println(response.StatusCode)
		}
		retry++
		if retry >= 10 {
			panic("Retry limit reached")
		}
		time.Sleep(time.Duration(int(time.Second) * 2 * retry))

	}
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

func check(err error) {
	if err != nil {
		panic(err)
	}
}
