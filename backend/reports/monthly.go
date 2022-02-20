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
	filter := getPrevMonthFilter()
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

	return fmt.Sprintf("Previous month spending: %f", sum), nil
}

func getPrevMonthFilter() string {
	date := time.Now().AddDate(0, -1, 0)
	str := date.Format("200601")
	return fmt.Sprintf("t=%s", str)
}
