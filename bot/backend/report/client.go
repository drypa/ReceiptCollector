package report

import (
	"context"
	"fmt"
	"time"

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

const reportsReconnectDelay = 5 * time.Second

func (c *Client) subscribeOnReports() {
	for {
		err := c.subscribe()
		if err != nil {
			log.Printf("Report subscription lost: %v. Reconnecting in %v...", err, reportsReconnectDelay)
			time.Sleep(reportsReconnectDelay)
		}
	}
}

func (c *Client) subscribe() error {
	ctx := context.Background()
	report := *(c.report)
	stream, err := report.GetReports(ctx, &inside.NoParams{})
	if err != nil {
		return fmt.Errorf("GetReports() failed: %w", err)
	}
	for {
		r, err := stream.Recv()
		if err != nil {
			return fmt.Errorf("Recv() failed: %w", err)
		}
		log.Printf("Send report %v", r)
		c.Notifications <- r
	}
}
