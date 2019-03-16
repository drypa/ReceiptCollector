package main

import (
	"net/url"
	"time"
)

type ParseResult struct {
	FiscalSign string
	Fd         string
	Fp         string
	Time       time.Time
	Sum        string
	N          string
}

func ParseReceipt(params string) ParseResult {
	values, err := url.ParseQuery(params)
	if err != nil {
		panic(err)
	}

	timeString := values["t"][0]

	timeParsed := parseAsTime(timeString)

	return ParseResult{
		N:          values["n"][0],
		FiscalSign: values["fp"][0],
		Sum:        values["s"][0],
		Fd:         values["fn"][0],
		Time:       timeParsed,
		Fp:         values["i"][0],
	}
}
