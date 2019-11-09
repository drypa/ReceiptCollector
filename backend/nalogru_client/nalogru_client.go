package nalogru_client

import (
	"fmt"
	"html/template"
	"io/ioutil"
	"log"
	"net/http"
	"net/url"
	"time"
)

type NalogruClient struct {
	BaseAddress string
	Login       string
	Password    string
}

func (nalogruClient NalogruClient) GetRawReceipt(receiptParams ParseResult) ([]byte, error) {
	odfsUrl := buildOfdsUrl(nalogruClient.BaseAddress, receiptParams)
	log.Println(odfsUrl)
	kktUrl := BuildKktsUrl(nalogruClient.BaseAddress, receiptParams)
	log.Println(kktUrl)
	_ := nalogruClient.SendOdfsRequest(odfsUrl)
	bytes, err := nalogruClient.SendKktsRequest(kktUrl)
	return bytes, err
}

func parseQuery(queryString string) (ParseResult, error) {
	form, err := url.ParseQuery(queryString)
	if err != nil {
		return ParseResult{}, err
	}
	timeString := form.Get("t")

	timeParsed := parseAsTime(timeString)

	return ParseResult{
		N:          template.HTMLEscapeString(form.Get("n")),
		FiscalSign: template.HTMLEscapeString(form.Get("fp")),
		Sum:        template.HTMLEscapeString(form.Get("s")),
		Fd:         template.HTMLEscapeString(form.Get("fn")),
		Time:       timeParsed,
		Fp:         template.HTMLEscapeString(form.Get("i")),
	}, nil
}

func (nalogruClient NalogruClient) SendOdfsRequest(queryString string) error {
	parseResult, err := parseQuery(queryString)
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
	fmt.Printf("ODFS request status: %d and body: %s \n", response.StatusCode, string(bytes))
	return nil
}

func (nalogruClient NalogruClient) SendKktsRequest(queryString string) ([]byte, error) {
	retry := 0
	client := &http.Client{}
	parseResult, err := parseQuery(queryString)
	if err != nil {
		return nil, err
	}

	kktsUrl := BuildKktsUrl(nalogruClient.BaseAddress, parseResult)
	for {
		response, err := sendRequest(kktsUrl, client, nalogruClient.Login, nalogruClient.Password)
		if err == nil && response.StatusCode == 200 {
			return ioutil.ReadAll(response.Body)
		}
		log.Println(err)
		if response != nil {
			log.Println(response.StatusCode)
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
