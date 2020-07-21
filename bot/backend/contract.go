package backend

type registrationRequest struct {
	TelegramId int
}

type addReceiptRequest struct {
	UserId        string
	ReceiptString string
}

//User represents userId to TelegramId relation.
type User struct {
	UserId     string
	TelegramId int
}
