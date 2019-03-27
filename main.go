package main

import (
	"encoding/json"
	"fmt"
	"github.com/adjust/redismq"
	"io/ioutil"
	"net/http"
	"net/url"
	"os"
	"time"
)

//const dumpDirectory = "./dump/"

func main() {
	login := os.Getenv("NALOGRU_LOGIN")
	password := os.Getenv("NALOGRU_PASS")
	rawReceiptQueue := redismq.CreateQueue("localhost", "3679", "", 6, "raw-receipts")
	const baseAddress = "https://proverkacheka.nalog.ru:9999"

	go consumeRawReceipts(rawReceiptQueue)

	http.HandleFunc("/api/receipt/as-query", func(writer http.ResponseWriter, request *http.Request) {

		if request.Method != http.MethodPost {
			writer.WriteHeader(http.StatusNotFound)
			return
		}
		err := request.ParseForm()
		if err != nil {
			writer.WriteHeader(http.StatusBadRequest)
		}
		receiptParams := ParseQuery(&request.Form)
		fmt.Println(receiptParams)

		rawReceipt, err := GetRawReceipt(baseAddress, receiptParams, login, password)
		check(err)
		saveResponse(rawReceiptQueue, rawReceipt)

	})
	address := ":8888"
	fmt.Printf("Starting http server at: \"%s\"...", address)
	fmt.Println(http.ListenAndServe(address, nil))

}

func saveResponse(queue *redismq.Queue, response []byte) {
	err := queue.Put(string(response))
	check(err)
}

//func GetReceipt(baseAddress string, receiptParams ParseResult, login string, password string) Receipt {
//	bytes, err := GetRawReceipt(baseAddress, receiptParams, login, password)
//	check(err)
//	dumpToFile(bytes)
//	receipt := parseReceipt(bytes)
//	return receipt
//}

func check(err error) {
	if err != nil {
		panic(err)
	}
}

//func dumpToFile(rawReceipt []byte) {
//	uuid, _ := uuid.NewUUID()
//	err := ioutil.WriteFile(dumpDirectory+uuid.String()+".json", rawReceipt, os.ModeType)
//	check(err)
//}

func GetRawReceipt(baseAddress string, receiptParams ParseResult, login string, password string) ([]byte, error) {
	odfsUrl := BuildOfdsUrl(baseAddress, receiptParams)
	fmt.Println(odfsUrl)
	kktUrl := BuildKktsUrl(baseAddress, receiptParams)
	fmt.Println(kktUrl)
	client := &http.Client{}
	sendOdfsRequest(odfsUrl, client, login, password)
	bytes, err := sendKktsRequest(kktUrl, client, login, password)
	return bytes, err
}

func ParseQuery(form *url.Values) ParseResult {
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
	check(err)

	res := receipt["document"]["receipt"]

	return res
}

func sendOdfsRequest(url string, client *http.Client, login string, password string) {
	response, err := sendRequest(url, client, login, password)
	check(err)

	fmt.Println(response.StatusCode)
}

func sendKktsRequest(url string, client *http.Client, login string, password string) ([]byte, error) {
	retry := 0
	for {
		response, err := sendRequest(url, client, login, password)
		if err == nil && response.StatusCode == 200 {
			return ioutil.ReadAll(response.Body)
		}
		retry++
		if retry >= 10 {
			panic("Retry limit reached")
		}
		time.Sleep(time.Duration(1000 * retry))

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

func consumeRawReceipts(rawQueue *redismq.Queue) {
	consumer, err := rawQueue.AddConsumer("receipt-parser")
	check(err)

	for {
		message, err := consumer.Get()
		check(err)

		receipt := parseReceipt([]byte(message.Payload))

		fmt.Println(receipt.DateTime)
		fmt.Println(receipt.Items)
		fmt.Println(receipt.RetailPlaceAddress)
		fmt.Println(receipt.TotalSum)
		fmt.Println(receipt.UserInn)
	}
}
