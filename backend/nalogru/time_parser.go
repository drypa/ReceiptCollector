package nalogru

import (
	"time"
)

//Парсит строку формата yyyyMMddThhmm
func parseAsTime(timeString string) (time.Time, error) {
	layoutWithSeconds := "20060102T150405"
	layoutWithoutSeconds := "20060102T1504"
	res, err := time.ParseInLocation(layoutWithSeconds, timeString, time.Local)
	if err == nil {
		return res, err
	}
	return time.ParseInLocation(layoutWithoutSeconds, timeString, time.Local)
}
