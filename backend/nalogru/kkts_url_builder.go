package nalogru

import (
	"fmt"
	"receipt_collector/nalogru/qr"
)

func BuildKktsUrl(baseAddress string, params qr.Query) string {
	return fmt.Sprintf("%s/v1/inns/*/kkts/*/fss/%s/tickets/%s?fiscalSign=%s&sendToEmail=no",
		baseAddress,
		params.Fd,
		params.Fp,
		params.FiscalSign)
}
