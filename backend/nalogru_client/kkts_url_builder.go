package nalogru_client

import "fmt"

func BuildKktsUrl(baseAddress string, params ParseResult) string {
	return fmt.Sprintf("%s/v1/inns/*/kkts/*/fss/%s/tickets/%s?fiscalSign=%s&sendToEmail=no",
		baseAddress,
		params.Fd,
		params.Fp,
		params.FiscalSign)
}
