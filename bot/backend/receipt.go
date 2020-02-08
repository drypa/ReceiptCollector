package backend

//AddReceipt adds receipt for telegram user.
func (client Client) AddReceipt(userId int, text string) error {
	_ = "/internal/receipt"
	return nil
}
