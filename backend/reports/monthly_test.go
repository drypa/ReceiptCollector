package reports

import (
	"testing"
	"time"
)

func Test_getPrevMonthFilter(t *testing.T) {
	year := 2021
	testData := map[int]string{
		1:  "t=202101",
		9:  "t=202109",
		10: "t=202110",
		11: "t=202111",
		12: "t=202112",
	}

	for key, val := range testData {
		actual := getPrevMonthFilter(year, key)
		if actual != val {
			t.Errorf("Filter for %d failed. expected %s, actual %s", key, val, actual)
		}
	}
}

type month struct {
	year  int
	month int
}

func Test_getPrevMonth(t *testing.T) {
	testData := map[time.Time]month{
		time.Date(2022, 01, 01, 0, 0, 0, 0, time.UTC): {2021, 12},
		time.Date(2022, 01, 01, 0, 0, 0, 0, time.UTC): {2021, 12},
		time.Date(2022, 01, 03, 0, 0, 0, 0, time.UTC): {2021, 12},
		time.Date(2022, 01, 31, 0, 0, 0, 0, time.UTC): {2021, 12},
		time.Date(2022, 02, 28, 0, 0, 0, 0, time.UTC): {2022, 1},
		time.Date(2022, 02, 01, 0, 0, 0, 0, time.UTC): {2022, 1},
	}
	for k, v := range testData {
		y, m := getPrevMonth(&k)
		if y != v.year {
			t.Errorf("For %s expected year %d but actual %d", k.String(), v.year, y)
		}
		if m != v.month {
			t.Errorf("For %s expected month %d but actual %d", k.String(), v.month, m)
		}
	}
}
