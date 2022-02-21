package report

import (
	"context"
	inside "github.com/drypa/ReceiptCollector/api/inside"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials"
	"log"
)

type Client struct {
	report        *inside.ReportApiClient
	Notifications chan *inside.Report
}

func New(backendUrl string, creds credentials.TransportCredentials) *Client {
	dial, err := grpc.Dial(backendUrl, grpc.WithTransportCredentials(creds))
	if err != nil {
		log.Printf("Failed to create connection with %s. Error: %v", backendUrl, err)
	}
	report := inside.NewReportApiClient(dial)
	notifications := make(chan *inside.Report)

	c := &Client{report: &report, Notifications: notifications}
	go c.subscribeOnReports()
	return c
}

func (c *Client) subscribeOnReports() {
	ctx := context.Background()
	report := *(c.report)
	stream, err := report.GetReports(ctx, &inside.NoParams{}, grpc.EmptyCallOption{})
	if err != nil {
		log.Fatalf("%v.GetReports() failed with %v", c.report, err)
	}
	for {
		report, err := stream.Recv()
		if err != nil {
			log.Fatalf("%v.Recv() failed with %v", stream, err)
		}
		log.Printf("Send report %v", *report)
		c.Notifications <- report
	}

}
