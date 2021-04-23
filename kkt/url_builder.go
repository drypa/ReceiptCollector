package kkt

import (
	"fmt"
	"github.com/drypa/ReceiptCollector/kkt/qr"
	"net/url"
	"strings"
)

func buildCheckReceiptUrl(baseAddress string, queryString string) (string, error) {
	parse, err := qr.Parse(queryString)
	if err != nil {
		return "", err
	}
	dateStr := url.QueryEscape(parse.Time.Format("2006-01-02T15:04:05"))
	sum := strings.ReplaceAll(parse.Sum, ".", "")
	query := fmt.Sprintf("fsId=%s&operationType=1&documentId=%s&fiscalSign=%s&date=%s&sum=%s",
		parse.Fd,
		parse.Fp,
		parse.FiscalSign,
		dateStr,
		sum)

	return fmt.Sprintf("%s/v2/check/ticket?%s", baseAddress, query), nil
}
