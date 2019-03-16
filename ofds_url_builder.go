package main

import "fmt"

func BuildOfdsUrl(baseAddress string, parameters ParseResult) string {
	timeString := parameters.Time.Format("2006-01-02T15:04:05")
	return fmt.Sprintf("%s/v1/ofds/*/inns/*/fss/%s/operations/%s/tickets/%s?fiscalSign=%s&date=%s&sum=%s",
		baseAddress,
		parameters.Fd,
		parameters.N,
		parameters.Fp,
		parameters.FiscalSign,
		timeString,
		parameters.Sum)
}
