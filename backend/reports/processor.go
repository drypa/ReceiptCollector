package reports

import (
	"github.com/robfig/cron/v3"
)

type Processor struct {
	c chan UserReport
	s *Sender
}

//New Creates Processor instance.
func New() (Processor, error) {
	reports := make(chan UserReport)
	sender := NewSender(reports)
	processor := Processor{c: reports, s: &sender}

	cr := cron.New()
	_, err := cr.AddFunc("0 15 1 * *", processor.sendMonthlyReport)

	return processor, err
}

func (p *Processor) sendMonthlyReport() {
	//TODO: get users -> for each user get last month receipts -> aggregate all sums and send.

	p.c <- UserReport{
		message: "",
		userId:  "",
	}
}
