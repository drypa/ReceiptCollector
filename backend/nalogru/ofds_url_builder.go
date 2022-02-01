package nalogru

import (
	"fmt"
	"net/url"
	"receipt_collector/nalogru/qr"
)

func buildCheckReceiptUrl(baseAddress string, queryString string) (string, error) {
	parse, err := qr.Parse(queryString)
	if err != nil {
		return "", err
	}
	dateStr := url.QueryEscape(parse.Time.Format("2006-01-02T15:04:05"))
	sum := int32(parse.Sum * 100)
	query := fmt.Sprintf("fsId=%s&operationType=1&documentId=%s&fiscalSign=%s&date=%s&sum=%d",
		parse.Fd,
		parse.Fp,
		parse.FiscalSign,
		dateStr,
		sum)

	return fmt.Sprintf("%s/v2/check/ticket?%s", baseAddress, query), nil
}
