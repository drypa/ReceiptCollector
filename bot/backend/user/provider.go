package user

import "github.com/drypa/ReceiptCollector/bot/backend"

type Provider struct {
	telegramIdUserIdMap map[int]string
	client              backend.Client
}

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
