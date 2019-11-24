package nalogru_client

import (
	"testing"
	"time"
)

func Test(t *testing.T) {
	testData := map[string]time.Time{
		"20190409T1303": time.Date(2019, 4, 9, 13, 3, 0, 0, time.Local),
		"20010102T0304": time.Date(2001, 1, 2, 3, 4, 0, 0, time.Local),
		"20011231T0000": time.Date(2001, 12, 31, 0, 0, 0, 0, time.Local),
	}

	for key, val := range testData {
		result, err := parseAsTime(key)
		if err != nil || result != val {
			t.Errorf("Parse was incorrect got %s, want %s", result, val)
		}
	}

}
