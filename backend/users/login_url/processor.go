package login_url

import (
	"context"
	api "github.com/drypa/ReceiptCollector/api/inside"
	"receipt_collector/users"
	"time"
)

//LoginLinkProcessor provides method to return login link.
type LoginLinkProcessor struct {
	repository    *users.Repository
	linkGenerator users.LinkGenerator
}

//GetLoginLink returns login link for user in request.
func (p LoginLinkProcessor) GetLoginLink(ctx context.Context, in *api.GetLoginLinkRequest) (*api.LoginLinkResponse, error) {
	telegramId := in.TelegramId
	user, err := p.repository.GetByTelegramId(ctx, int(telegramId))
	if err != nil {
		return nil, err
	}
	url, err := p.linkGenerator.GetRedirectLink(user.Id.Hex())
	expiration := time.Now().Add(time.Minute * 15)
	err = p.repository.UpdateLoginLink(ctx, user.Id, url, expiration)
	if err != nil {
		return nil, err
	}
	return &api.LoginLinkResponse{
		Url:        url,
		Expiration: expiration.Unix(),
	}, nil
}
