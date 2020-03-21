package user

import "github.com/drypa/ReceiptCollector/bot/backend"

//Provider provides telegramId to userId mapping.
type Provider struct {
	telegramIdUserIdMap map[int]string
	client              backend.Client
}

//New constructs Provider instance.
func New(client backend.Client) (Provider, error) {
	users, err := client.GetUsers()
	if err != nil {
		return Provider{}, err
	}
	userIdMap := make(map[int]string, len(users))
	for _, u := range users {
		userIdMap[u.TelegramId] = u.UserId
	}
	return Provider{client: client, telegramIdUserIdMap: userIdMap}, nil
}

//GetUserId returns userId by telegramId.
func (provider Provider) GetUserId(telegramId int) (string, error) {
	id, ok := provider.telegramIdUserIdMap[telegramId]
	if ok == true {
		return id, nil
	}
	user, err := provider.client.GetUser(telegramId)
	if err != nil {
		return "", err
	}
	provider.telegramIdUserIdMap[telegramId] = user.UserId
	return user.UserId, nil
}
