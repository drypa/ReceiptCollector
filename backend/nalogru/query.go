package nalogru

import (
	"html/template"
	"net/url"
	"strconv"
	"time"
)

type Query struct {
	FiscalSign string
	Fd         string
	Fp         string
	Time       time.Time
	Sum        string
	N          string
}

func Parse(queryString string) (Query, error) {
	form, err := url.ParseQuery(queryString)
	res := Query{}
	if err != nil {
		return res, err
	}
	timeString := form.Get("t")

	timeParsed, err := parseAsTime(timeString)
	if err != nil {
		return res, err
	}

	return Query{
		N:          template.HTMLEscapeString(form.Get("n")),
		FiscalSign: template.HTMLEscapeString(form.Get("fp")),
		Sum:        template.HTMLEscapeString(form.Get("s")),
		Fd:         template.HTMLEscapeString(form.Get("fn")),
		Time:       timeParsed,
		Fp:         template.HTMLEscapeString(form.Get("i")),
	}, nil
}

func (query Query) Validate() error {
	_, errN := strconv.Atoi(query.N)
	_, errFs := strconv.Atoi(query.FiscalSign)
	_, errSum := strconv.ParseFloat(query.Sum, 64)
	_, errFd := strconv.Atoi(query.Fd)
	return firstError([]error{errN, errFs, errSum, errFd})
}
