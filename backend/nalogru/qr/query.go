package qr

import (
	"fmt"
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

//Parse converts query string from bar code to Query structure.
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

//ToString returns normalized representation of Query.
func (q *Query) ToString() string {
	t := q.Time.Format("20060102T1504")
	return fmt.Sprintf("t=%s&s=%s&fn=%s&i=%s&fp=%s&n=%s", t, q.Sum, q.Fd, q.Fp, q.FiscalSign, q.N)
}

func (q Query) Validate() error {
	_, errN := strconv.Atoi(q.N)
	_, errFs := strconv.Atoi(q.FiscalSign)
	_, errSum := strconv.ParseFloat(q.Sum, 64)
	_, errFd := strconv.Atoi(q.Fd)
	return firstError([]error{errN, errFs, errSum, errFd})
}

func firstError(errors []error) error {
	for _, err := range errors {
		if err != nil {
			return err
		}
	}
	return nil
}
