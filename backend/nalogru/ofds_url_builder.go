package nalogru

import (
	"fmt"
	"strings"
)

func buildOfdsUrl(baseAddress string, parameters Query) string {
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

func buildCheckReceiptUrl(baseAddress string, queryString string) (string, error) {
	parse, err := Parse(queryString)
	if err != nil {
		return "", err
	}
	dateStr := parse.Time.Format("2006-01-02T15:04:05")
	sum := strings.ReplaceAll(parse.Sum, ".", "")
	query := fmt.Sprintf("fsId=%s&operationType=1&documentId=%s&fiscalSign=%s&date=%s&sum=%s", parse.Fd, parse.Fp, parse.FiscalSign, dateStr, sum)

	return fmt.Sprintf("%s/v2/check/ticket?%s", baseAddress, query), nil
}
