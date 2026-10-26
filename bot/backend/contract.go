package backend

type registrationRequest struct {
	TelegramId int
}

type addReceiptRequest struct {
	UserId        string
	ReceiptString string
}

// User represents userId to TelegramId relation.
type User struct {
	UserId     string
	TelegramId int
	// Verified is true when the user registered AND confirmed the phone
	// with an SMS code (backend derives it from device tokens, ADR-029 D1).
	Verified bool
}
