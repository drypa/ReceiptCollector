package main

import (
	"encoding/json"
	"fmt"
	"io/ioutil"
	"net/http"
	"os"
	"time"
)

func main() {
	login := os.Getenv("NALOGRU_LOGIN")
	password := os.Getenv("NALOGRU_PASS")
	const baseAddress = "https://proverkacheka.nalog.ru:9999"
	if len(os.Args) > 1 {
		for _, query := range os.Args[1:] {
			queryString := ParseReceipt(query)

			odfsUrl := BuildOfdsUrl(baseAddress, queryString)
			fmt.Println(odfsUrl)
			kktUrl := BuildKktsUrl(baseAddress, queryString)
			fmt.Println(kktUrl)

			client := &http.Client{}
			sendOdfsRequest(odfsUrl, client, login, password)

			bytes, err := sendKktsRequest(kktUrl, client, login, password)
			if err != nil {
				panic(err)
			}
			receipt := parseReceipt(bytes)

			fmt.Println(receipt.DateTime)
			fmt.Println(receipt.Items)
			fmt.Println(receipt.RetailPlaceAddress)
			fmt.Println(receipt.TotalSum)
			fmt.Println(receipt.UserInn)
		}
	}

}

func parseReceipt(bytes []byte) Receipt {
	var receipt map[string]map[string]Receipt
	err := json.Unmarshal(bytes, &receipt)
	if err != nil {
		panic(err)
	}
	res := receipt["document"]["receipt"]

	return res
}

func sendOdfsRequest(url string, client *http.Client, login string, password string) {
	response, err := sendRequest(url, client, login, password)
	if err != nil {
		panic(err)
	}
	fmt.Println(response.StatusCode)
}

func sendKktsRequest(url string, client *http.Client, login string, password string) ([]byte, error) {

	for {
		response, err := sendRequest(url, client, login, password)
		if err == nil && response.StatusCode == 200 {
			return ioutil.ReadAll(response.Body)
		}
		time.Sleep(1000)
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
