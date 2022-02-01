package qr

import (
	"fmt"
	"html/template"
	"net/url"
	"strconv"
	"strings"
	"time"
)

type Query struct {
	FiscalSign string
	Fd         string
	Fp         string
	Time       time.Time
	Sum        float32
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

	s := template.HTMLEscapeString(form.Get("s"))
	sum, err := strconv.ParseFloat(s, 32)
	if err != nil {
		return res, err
	}
	if strings.Contains(s, ".") == false {
		sum = sum / 100
	}

	return Query{
		N:          template.HTMLEscapeString(form.Get("n")),
		FiscalSign: template.HTMLEscapeString(form.Get("fp")),
		Sum:        float32(sum),
		Fd:         template.HTMLEscapeString(form.Get("fn")),
		Time:       timeParsed,
		Fp:         template.HTMLEscapeString(form.Get("i")),
	}, nil
}

//ToString returns normalized representation of Query.
func (q *Query) ToString() string {
	t := q.Time.Format("20060102T1504")
	formattedSum := fmt.Sprintf("%.2f", q.Sum)
	return fmt.Sprintf("t=%s&s=%s&fn=%s&i=%s&fp=%s&n=%s", t, formattedSum, q.Fd, q.Fp, q.FiscalSign, q.N)
}

func (q Query) Validate() error {
	_, errN := strconv.Atoi(q.N)
	_, errFs := strconv.Atoi(q.FiscalSign)
	_, errFd := strconv.Atoi(q.Fd)
	return firstError([]error{errN, errFs, errFd})
}

func firstError(errors []error) error {
	for _, err := range errors {
		if err != nil {
			return err
		}
	}
	return nil
}
