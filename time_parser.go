package main

import (
	"strconv"
	"time"
)

//Парсит строку формата yyyyMMddThhmm
func parseAsTime(timeString string) time.Time {
	year, _ := strconv.ParseInt(timeString[0:4], 0, 64)
	month, _ := strconv.ParseInt(timeString[4:6], 0, 64)
	day, _ := strconv.ParseInt(timeString[6:8], 0, 64)
	hour, _ := strconv.ParseInt(timeString[9:11], 0, 64)
	minutes, _ := strconv.ParseInt(timeString[11:], 0, 64)

	result := time.Date(int(year), time.Month(int(month)), int(day), int(hour), int(minutes), 0, 0, time.Local)

	return result
}
