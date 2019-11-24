package nalogru

import "fmt"

func BuildKktsUrl(baseAddress string, params Query) string {
	return fmt.Sprintf("%s/v1/inns/*/kkts/*/fss/%s/tickets/%s?fiscalSign=%s&sendToEmail=no",
		baseAddress,
		params.Fd,
		params.Fp,
		params.FiscalSign)
}
