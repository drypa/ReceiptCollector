package worker

import (
	"log"
	"testing"
	"time"
)

//Test_getDurationToNextDay_LastDayOfMonth check interval for a last day of month.
func Test_getDurationToNextDay_LastDayOfMonth(t *testing.T) {
	now := time.Date(2021, time.February, 28, 21, 0, 0, 0, time.UTC)
	duration := getDurationToNextDay(now)
	if duration.Hours() != 3 {
		log.Printf("got %f hours\n", duration.Hours())
		t.Fail()
	}
}
