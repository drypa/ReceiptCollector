package users

import (
	"context"
	api "github.com/drypa/ReceiptCollector/api/inside"
	"google.golang.org/grpc"
	"receipt_collector/device"
	"receipt_collector/nalogru"
	nalogDevice "receipt_collector/nalogru/device"
	"time"
)

// Processor provides method to return login link.
type Processor struct {
	repository    *Repository
	linkGenerator LinkGenerator
	d             *device.Service
	nalogClient   *nalogru.Client
}

// NewProcessor constructs Processor.
func NewProcessor(repository *Repository, linkGenerator LinkGenerator, nalogClient *nalogru.Client, d *device.Service) *Processor {
	return &Processor{repository: repository, linkGenerator: linkGenerator, nalogClient: nalogClient, d: d}
}

// GetLoginLink returns login link for user in request.
func (p Processor) GetLoginLink(ctx context.Context, in *api.GetLoginLinkRequest) (*api.LoginLinkResponse, error) {
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

// GetUsers returns all registered users.
func (p Processor) GetUsers(ctx context.Context, req *api.NoParams) (*api.GetUsersResponse, error) {
	all, err := p.repository.GetAll(ctx)
	if err != nil {
		return nil, err
	}
	usersList := make([]*api.User, len(all))

	for i, v := range all {
		usersList[i] = &api.User{
			UserId:     v.Id.Hex(),
			TelegramId: int32(v.TelegramId),
		}
	}

	resp := api.GetUsersResponse{
		Users: usersList,
	}
	return &resp, err
}

// GetUser get user by telegramId.
func (p Processor) GetUser(ctx context.Context, in *api.GetUserRequest, opts ...grpc.CallOption) (*api.GetUserResponse, error) {
	user, err := p.repository.GetByTelegramId(ctx, int(in.TelegramId))
	if err != nil {
		return nil, err
	}
	response := api.GetUserResponse{
		User: &api.User{
			UserId:     user.Id.Hex(),
			TelegramId: int32(user.TelegramId),
		},
	}
	return &response, err
}

func (p Processor) RegisterUser(ctx context.Context, in *api.UserRegistrationRequest, opts ...grpc.CallOption) (*api.UserRegistrationResponse, error) {
	user, err := p.repository.GetByTelegramId(ctx, int(in.TelegramId))
	if err != nil {
		return nil, err
	}
	if user == nil {
		user, err = p.addNewUser(ctx, int(in.TelegramId))
		if err != nil {
			return nil, err
		}
	}
	userId := user.Id.Hex()

	d := &nalogDevice.Device{
		ClientSecret: "", //TODO: generate random(or not) secret
		SessionId:    "",
		RefreshToken: "",
		Update:       nil,
		UserId:       userId,
		Phone:        in.PhoneNumber,
	}
	d.Update = func(sessionId string, refreshToken string) error {
		return p.d.Update(ctx, d, sessionId, refreshToken)
	}

	err = p.d.Add(ctx, d)
	if err != nil {
		return nil, err
	}
	err = p.nalogClient.AuthByPhone(d)

	return &api.UserRegistrationResponse{UserId: userId}, nil
}

func (p Processor) addNewUser(ctx context.Context, telegramId int) (*User, error) {
	u := User{
		TelegramId: telegramId,
	}
	err := p.repository.Insert(ctx, &u)
	if err != nil {
		return nil, err
	}
	return &u, nil
}
