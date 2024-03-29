package reports

import (
	"context"
	"fmt"
	"log"
	"receipt_collector/nalogru/qr"
	"receipt_collector/reports/dal"
	"time"
)

type Monthly struct {
	r *dal.Repository
}

//NewMonthly creates new Monthly reporter.
func NewMonthly(r *dal.Repository) *Monthly {
	return &Monthly{r: r}
}

//GetCronSpec defines cron spec for reporter.
func (m *Monthly) GetCronSpec() string {
	return "0 15 1 * *"
}

//GetReport returns monthly report for user.
func (m *Monthly) GetReport(ctx context.Context, userId string) (string, error) {
	now := time.Now()
	year, month := getPrevMonth(&now)
	filter := getPrevMonthFilter(year, month)
	sum, err := getSumByFilter(ctx, userId, m.r, filter)
	if err != nil {
		return "", err
	}

	return fmt.Sprintf("Your expenses for %s %d : %.2f", time.Month(month).String(), year, sum), nil
}

func getSumByFilter(ctx context.Context, userId string, r *dal.Repository, filter string) (float64, error) {
	receipts, err := r.GetByQueryStringFilter(ctx, userId, filter)
	if err != nil {
		log.Printf("Failed to get receipts for user %s by filter %s\n", userId, filter)
		return 0, err
	}
	var sum = 0.0
	for _, v := range receipts {
		query, err := qr.Parse(v.QueryString)
		if err != nil {
			log.Printf("Failed to parse '%s' for user %s\n", v.QueryString, userId)
			return 0, err
		}
		sum = sum + float64(query.Sum)
	}
	return sum, nil
}

func getPrevMonth(t *time.Time) (y int, m int) {
	date := t.AddDate(0, -1, 0)
	return date.Year(), int(date.Month())
}

func getPrevMonthFilter(year int, month int) string {
	return fmt.Sprintf("t=%d%02d", year, month)
}
