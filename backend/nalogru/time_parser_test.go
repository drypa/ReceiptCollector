package nalogru

import (
	"testing"
	"time"
)

func Test_parseAsTime_success(t *testing.T) {
	testData := map[string]time.Time{
		"20190409T1303":   time.Date(2019, 4, 9, 13, 3, 0, 0, time.Local),
		"20010102T0304":   time.Date(2001, 1, 2, 3, 4, 0, 0, time.Local),
		"20011231T0000":   time.Date(2001, 12, 31, 0, 0, 0, 0, time.Local),
		"20011231T000000": time.Date(2001, 12, 31, 0, 0, 0, 0, time.Local),
	}

	for key, val := range testData {
		result, err := parseAsTime(key)
		if err != nil || result != val {
			t.Errorf("Parse was incorrect got %s, want %s", result, val)
		}
	}
}

func Test_parseAsTime_error(t *testing.T) {
	testData := []string{
		"",
		"0",
		"abc",
		"2001-12-31T00:00:00",
		"2006-01-02T15:04:05-0700",
		"Mon, 2 Jan 2006 15:04:05 MST",
		"Mon, 2 Jan 2006 15:04:05 MST",
		"20190409T1303&s=333.00&fn=8710000101407564&i=91331&fp=865669710&n=1",
		"t=20190409T1303&s=333.00&fn=8710000101407564&i=91331&fp=865669710&n=1",
	}

	for _, val := range testData {
		result, err := parseAsTime(val)
		if err == nil {
			t.Errorf("Parse incorrect string %s got %s", val, result)
		}
	}
}
