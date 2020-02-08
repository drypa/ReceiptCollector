package users

type registrationRequest struct {
	TelegramId int
}

type addReceiptRequest struct {
	TelegramId    int
	ReceiptString string
}
