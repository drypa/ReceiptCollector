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

func NewMonthly(r *dal.Repository) *Monthly {
	return &Monthly{r: r}
}
func (m *Monthly) GetCronSpec() string {
	return "0 15 1 * *"
}

func (m *Monthly) GetReport(ctx context.Context, userId string) (string, error) {
	year, month := getPrevMonth()
	filter := getPrevMonthFilter(year, month)
	receipts, err := m.r.GetByQueryStringFilter(ctx, userId, filter)
	if err != nil {
		log.Printf("Failed to get receipts for user %s by filter %s\n", userId, filter)
		return "", err
	}
	var sum = 0.0
	for _, v := range receipts {
		query, err := qr.Parse(v.QueryString)
		if err != nil {
			log.Printf("Failed to parse '%s' for user %s\n", v.QueryString, userId)
			return "", err
		}
		sum = sum + float64(query.Sum)
	}

	return fmt.Sprintf("Your expenses for %s %d : %f", time.Month(month).String(), year, sum), nil
}

func getPrevMonth() (y int, m int) {
	date := time.Now().AddDate(0, -1, 0)
	return date.Year(), int(date.Month())
}

func getPrevMonthFilter(year int, month int) string {
	return fmt.Sprintf("t=%d%02d", year, month)
}
