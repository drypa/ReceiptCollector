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
	return &Client{report: &report, Notifications: notifications}
}

func (c *Client) GetReports(ctx context.Context, in *inside.NoParams, opts ...grpc.CallOption) (inside.ReportApi_GetReportsClient, error) {
	stream, err := c.GetReports(ctx, in, opts...)
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
