package reports

import (
	"context"
	"fmt"
	"receipt_collector/reports/dal"
	"time"
)

//Annual is annual reporter.
type Annual struct {
	r *dal.Repository
}

//NewAnnual creates annual reporter.
func NewAnnual(r *dal.Repository) *Annual {
	return &Annual{r: r}
}

//GetCronSpec defines cron spec for reporter.
func (m *Annual) GetCronSpec() string {
	return "0 15 1 1 *"
}

//GetReport returns annual report for user.
func (m *Annual) GetReport(ctx context.Context, userId string) (string, error) {
	year := time.Now().Year() - 1

	filter := getFilter(year)
	sum, err := getSumByFilter(ctx, userId, m.r, filter)
	if err != nil {
		return "", err
	}
	return fmt.Sprintf("Your expenses for %d : %.2f", year, sum), nil
}

func getFilter(year int) string {
	return fmt.Sprintf("t=%d", year)
}
