package workers

import (
	"log"
	"os"
	"strconv"
	"time"
)

const intervalEnvironmentVariable = "GET_RECEIPT_WORKER_INTERVAL"

const workerStartHourEnvironmentVariable = "WORKER_START_HOUR"
const workerEndHourEnvironmentVariable = "WORKER_END_HOUR"

type Settings struct {
	Start    int
	End      int
	Interval time.Duration
}

func ReadFromEnvironment() Settings {
	workerIntervalString := os.Getenv(intervalEnvironmentVariable)

	startString := os.Getenv(workerStartHourEnvironmentVariable)
	start, err := strconv.Atoi(startString)
	if err != nil {
		log.Printf("Error while parse %s variable value=%s. Error: %v", workerStartHourEnvironmentVariable, startString, err)
		start = 0
	}
	endString := os.Getenv(workerEndHourEnvironmentVariable)
	end, err := strconv.Atoi(endString)
	if err != nil {
		log.Printf("Error while parse %s variable value=%s. Error: %v", workerEndHourEnvironmentVariable, endString, err)
		end = 0
	}
	processingInterval, err := time.ParseDuration(workerIntervalString)
	if err != nil {
		log.Printf("invalid '%s' value: %s", intervalEnvironmentVariable, workerIntervalString)
		processingInterval = time.Minute
		log.Println("processing interval is set to 1 minute")
	}

	settings := Settings{
		Start:    start,
		End:      end,
		Interval: processingInterval,
	}
	return settings
}
