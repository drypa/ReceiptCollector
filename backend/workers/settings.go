package workers

import (
	"log"
	"os"
	"time"
)

const intervalEnvironmentVariable = "GET_RECEIPT_WORKER_INTERVAL"

// Settings for Worker.
type Settings struct {
	Interval time.Duration
}

// ReadFromEnvironment creates Settings from environment variables.
func ReadFromEnvironment() Settings {
	workerIntervalString := os.Getenv(intervalEnvironmentVariable)

	processingInterval, err := time.ParseDuration(workerIntervalString)
	if err != nil {
		log.Printf("invalid '%s' value: %s", intervalEnvironmentVariable, workerIntervalString)
		processingInterval = time.Minute
		log.Println("processing interval is set to 1 minute")
	}

	settings := Settings{
		Interval: processingInterval,
	}
	return settings
}
