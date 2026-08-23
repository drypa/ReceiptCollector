module github.com/drypa/ReceiptCollector/bot

go 1.27.0

require (
	github.com/drypa/ReceiptCollector/api/inside v0.0.0-20260823130540-01f9edfb2cc4
	github.com/go-telegram-bot-api/telegram-bot-api v4.6.4+incompatible
	google.golang.org/grpc v1.83.1
)

require (
	github.com/technoweenie/multipartstreamer v1.0.1 // indirect
	golang.org/x/net v0.58.0 // indirect
	golang.org/x/sys v0.47.0 // indirect
	golang.org/x/text v0.41.0 // indirect
	google.golang.org/genproto/googleapis/rpc v0.0.0-20260819154853-08b0e4226688 // indirect
	google.golang.org/protobuf v1.36.12 // indirect
)

replace github.com/drypa/ReceiptCollector/api/inside => ../api/inside
