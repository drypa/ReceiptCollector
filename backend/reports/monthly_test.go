package reports

import "testing"

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
