package main

import (
	"strconv"
	"time"
)

//Парсит строку формата yyyyMMddThhmm
func parseAsTime(timeString string) time.Time {
	year, _ := strconv.Atoi(timeString[0:4])
	month, _ := strconv.Atoi(timeString[4:6])
	day, _ := strconv.Atoi(timeString[6:8])
	hour, _ := strconv.Atoi(timeString[9:11])
	minutes, _ := strconv.Atoi(timeString[11:])

	result := time.Date(int(year), time.Month(int(month)), int(day), int(hour), int(minutes), 0, 0, time.Local)

	return result
}
