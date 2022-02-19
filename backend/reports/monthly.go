package reports

type Monthly struct {
}

func NewMonthly() *Monthly {
	return &Monthly{}
}
func (m *Monthly) GetCronSpec() string {
	return "0 15 1 * *"
}

func (m *Monthly) GetSum(userId string) float64 {
	//TODO: return sum
	return 1
}

func (m *Monthly) GetReport(userId string) string {
	return "TODO: return report message text"
}
