package reports

import (
	"context"
	"github.com/robfig/cron/v3"
	"log"
	"receipt_collector/users"
)

//Processor send notifications with statistics.
type Processor struct {
	c               chan UserReport
	s               *Sender
	cr              *cron.Cron
	jobs            []cron.EntryID
	usersRepository *users.Repository
}

//New Creates Processor instance and start all jobs.
func New(r *users.Repository) (Processor, error) {
	reports := make(chan UserReport)
	sender := NewSender(reports)
	cr := cron.New()
	p := Processor{c: reports, s: &sender, cr: cr, usersRepository: r}

	aggregators := make([]Aggregator, 0)
	aggregators = append(aggregators, NewMonthly())

	for _, v := range aggregators {
		err := p.addJob(v.GetCronSpec(), p.getJobFunc(v))
		if err != nil {
			return p, err
		}
	}

	cr.Start()
	return p, nil
}

func (p *Processor) getJobFunc(aggregator Aggregator) func() {
	all, err := p.usersRepository.GetAll(context.Background())
	if err != nil {
		log.Fatalf("Failed to get users for Report.%v \n", err)
	}
	return func() {
		for _, v := range all {
			report := aggregator.GetReport(v.Id.Hex())

			p.c <- UserReport{
				message:    report,
				telegramId: int64(v.TelegramId),
			}
		}
	}
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
