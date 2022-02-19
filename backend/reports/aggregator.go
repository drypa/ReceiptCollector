package reports

type Aggregator interface {
	GetCronSpec() string
	GetSum(userId string) float64
	GetReport(userId string) string
}
