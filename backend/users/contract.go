package users

type registrationRequest struct {
	TelegramId int
}

type user struct {
	UserId     string
	TelegramId int
}
