package main

import (
	"encoding/json"
	"fmt"
	"io/ioutil"
	"net/http"
	"net/url"
	"os"
	"time"
)

func main() {

	http.HandleFunc("/api/receipt/as-query", func(writer http.ResponseWriter, request *http.Request) {
		login := os.Getenv("NALOGRU_LOGIN")
		password := os.Getenv("NALOGRU_PASS")
		const baseAddress = "https://proverkacheka.nalog.ru:9999"

		if request.Method != http.MethodPost {
			writer.WriteHeader(http.StatusNotFound)
			return
		}
		err := request.ParseForm()
		if err != nil {
			writer.WriteHeader(http.StatusBadRequest)
		}
		receiptParams := ParseReceipt(&request.Form)
		fmt.Println(receiptParams)

		odfsUrl := BuildOfdsUrl(baseAddress, receiptParams)
		fmt.Println(odfsUrl)
		kktUrl := BuildKktsUrl(baseAddress, receiptParams)
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
	})
	fmt.Println(http.ListenAndServe("0.0.0.0:8888", nil))

}

func ParseReceipt(form *url.Values) ParseResult {
	timeString := form.Get("t")

	timeParsed := parseAsTime(timeString)

	return ParseResult{
		N:          form.Get("n"),
		FiscalSign: form.Get("fp"),
		Sum:        form.Get("s"),
		Fd:         form.Get("fn"),
		Time:       timeParsed,
		Fp:         form.Get("i"),
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
