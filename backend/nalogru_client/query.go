package nalogru_client

import (
	"html/template"
	"net/url"
)

func Parse(queryString string) (Query, error) {
	form, err := url.ParseQuery(queryString)
	if err != nil {
		return Query{}, err
	}
	timeString := form.Get("t")

	timeParsed := parseAsTime(timeString)

	return Query{
		N:          template.HTMLEscapeString(form.Get("n")),
		FiscalSign: template.HTMLEscapeString(form.Get("fp")),
		Sum:        template.HTMLEscapeString(form.Get("s")),
		Fd:         template.HTMLEscapeString(form.Get("fn")),
		Time:       timeParsed,
		Fp:         template.HTMLEscapeString(form.Get("i")),
	}, nil
}

func Validate(query Query) error {
	//TODO: need implement format validation
	return nil
}
