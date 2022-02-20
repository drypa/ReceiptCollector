package reports

import "context"

type Aggregator interface {
	GetCronSpec() string
	GetReport(ctx context.Context, userId string) (string, error)
}
