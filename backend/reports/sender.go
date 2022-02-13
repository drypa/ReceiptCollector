package reports

import (
	api "github.com/drypa/ReceiptCollector/api/inside"
	"log"
)

//Sender send reports.
type Sender struct {
	api.UnimplementedReportApiServer
	c <-chan UserReport
}

//NewSender creates sender
func NewSender(c <-chan UserReport) Sender {
	return Sender{c: c}
}

func (s *Sender) GetReports(_ *api.NoParams, server api.ReportApi_GetReportsServer) error {
	c := s.c
	for {
		select {
		case <-server.Context().Done():
			log.Printf("Server was stoped")
			return nil
		case i := <-c:
			report := api.Report{
				Message: i.message,
				UserId:  i.userId,
			}
			err := server.Send(&report)
			if err != nil {
				log.Printf("Failed to send report to user %s with error: %v", i.userId, err)
			}
		}
	}
}
