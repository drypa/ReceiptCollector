module receipt_collector

go 1.27.0

require (
	github.com/drypa/ReceiptCollector/api/inside v0.0.0-20260322103942-d1abb1e46beb
	github.com/go-co-op/gocron v1.28.2
	github.com/goji/httpauth v0.0.0-20160601135302-2da839ab0f4d
	github.com/robfig/cron/v3 v3.0.1
	go.mongodb.org/mongo-driver v1.17.9
	golang.org/x/crypto v0.26.0
	google.golang.org/grpc v1.54.0
)

require (
	github.com/golang/protobuf v1.5.2 // indirect
	github.com/golang/snappy v0.0.4 // indirect
	github.com/klauspost/compress v1.16.7 // indirect
	github.com/montanaflynn/stats v0.7.1 // indirect
	github.com/xdg-go/pbkdf2 v1.0.0 // indirect
	github.com/xdg-go/scram v1.1.2 // indirect
	github.com/xdg-go/stringprep v1.0.4 // indirect
	github.com/youmark/pkcs8 v0.0.0-20240726163527-a2c0da244d78 // indirect
	go.uber.org/atomic v1.9.0 // indirect
	golang.org/x/net v0.21.0 // indirect
	golang.org/x/sync v0.8.0 // indirect
	golang.org/x/sys v0.23.0 // indirect
	golang.org/x/text v0.17.0 // indirect
	google.golang.org/genproto v0.0.0-20230110181048-76db0878b65f // indirect
	google.golang.org/protobuf v1.28.1 // indirect
)

replace github.com/drypa/ReceiptCollector/api/inside => ../api/inside
