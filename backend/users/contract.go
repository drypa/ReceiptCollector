package users

type registrationRequest struct {
	TelegramId int32
}

type user struct {
	UserId     string
	TelegramId int
}
