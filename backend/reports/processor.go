package reports

import (
	"github.com/robfig/cron/v3"
)

//Processor send notifications with statistics.
type Processor struct {
	c    chan UserReport
	s    *Sender
	cr   *cron.Cron
	jobs []cron.EntryID
}

//New Creates Processor instance and start all jobs.
func New() (Processor, error) {
	reports := make(chan UserReport)
	sender := NewSender(reports)
	cr := cron.New()
	p := Processor{c: reports, s: &sender, cr: cr}

	err := p.addJob("0 15 1 * *", p.sendMonthlyReport)

	cr.Start()
	return p, err
}

func (p *Processor) addJob(spec string, task func()) error {
	monthly, err := p.cr.AddFunc(spec, task)
	p.jobs = append(p.jobs, monthly)
	return err
}

//Stop all jobs.
func (p *Processor) Stop() {
	for _, v := range p.jobs {
		p.cr.Remove(v)
	}
	p.cr.Stop()
}

func (p *Processor) sendMonthlyReport() {
	//TODO: get users -> for each user get last month receipts -> aggregate all sums and send.

	p.c <- UserReport{
		message:    "ok",
		telegramId: 1,
	}
}
