package nalogru

import (
	"strconv"
	"time"
)

//Парсит строку формата yyyyMMddThhmm
func parseAsTime(timeString string) (time.Time, error) {
	year, errYear := strconv.Atoi(timeString[0:4])
	month, errMonth := strconv.Atoi(timeString[4:6])
	day, errDay := strconv.Atoi(timeString[6:8])
	hour, errHour := strconv.Atoi(timeString[9:11])
	minutes, errMinutes := strconv.Atoi(timeString[11:])

	result := time.Date(int(year), time.Month(int(month)), int(day), int(hour), int(minutes), 0, 0, time.Local)

	return result, firstError([]error{errYear, errMonth, errDay, errHour, errMinutes})
}

func firstError(errors []error) error {
	for _, err := range errors {
		if err != nil {
			return err
		}
	}
	return nil
}
