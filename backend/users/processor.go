package users

import (
	"context"
	api "github.com/drypa/ReceiptCollector/api/inside"
	"google.golang.org/grpc"
	"log"
	"receipt_collector/device"
	"receipt_collector/nalogru"
	nalogDevice "receipt_collector/nalogru/device"
)

// isDeviceConfirmed reports whether the device proves successful phone
// confirmation (SMS code): non-empty SessionId or RefreshToken is written
// only by a successful VerifyPhone (ADR-029 D1). A nil device is not confirmed.
func isDeviceConfirmed(d *nalogDevice.Device) bool {
	return d != nil && (d.SessionId != "" || d.RefreshToken != "")
}

// confirmedUserIds returns the set of userIds having at least one confirmed
// device (a user may own several devices; any confirmed one is enough).
func confirmedUserIds(devices []*nalogDevice.Device) map[string]bool {
	confirmed := make(map[string]bool)
	for _, d := range devices {
		if isDeviceConfirmed(d) {
			confirmed[d.UserId] = true
		}
	}
	return confirmed
}

// Processor handles user operations: retrieval, registration and phone verification via nalog.ru.
type Processor struct {
	repository    *Repository
	deviceService *device.Service
	nalogClient   *nalogru.Client
	clientSecret  string
}

// NewProcessor constructs Processor.
func NewProcessor(repository *Repository, nalogClient *nalogru.Client, d *device.Service, secret string) *Processor {
	return &Processor{
		repository:    repository,
		nalogClient:   nalogClient,
		deviceService: d,
		clientSecret:  secret,
	}
}

// GetUsers returns all registered users.
// Verified flag is computed in-memory from device.Service (no Mongo access).
func (p Processor) GetUsers(ctx context.Context, _ *api.NoParams) (*api.GetUsersResponse, error) {
	all, err := p.repository.GetAll(ctx)
	if err != nil {
		return nil, err
	}
	confirmed := confirmedUserIds(p.deviceService.All(ctx))
	usersList := make([]*api.User, len(all))

	for i, v := range all {
		userId := v.Id.Hex()
		usersList[i] = &api.User{
			UserId:     userId,
			TelegramId: int32(v.TelegramId),
			Verified:   confirmed[userId],
		}
	}

	resp := api.GetUsersResponse{
		Users: usersList,
	}
	return &resp, err
}

// GetUser get user by telegramId.
func (p Processor) GetUser(ctx context.Context, in *api.GetUserRequest, _ ...grpc.CallOption) (*api.GetUserResponse, error) {
	user, err := p.repository.GetByTelegramId(ctx, int(in.TelegramId))
	if err != nil {
		return nil, err
	}
	userId := user.Id.Hex()
	verified := false
	d, err := p.deviceService.GetByUserId(ctx, userId)
	if err != nil {
		// Device lookup failure must not fail GetUser: degrade to verified=false
		// (same degradation class as "status service down = no keyboard", ADR-029 D1).
		log.Printf("Failed to get device for user %s: %v. Treating user as not verified", userId, err)
	} else {
		verified = isDeviceConfirmed(d)
	}
	response := api.GetUserResponse{
		User: &api.User{
			UserId:     userId,
			TelegramId: int32(user.TelegramId),
			Verified:   verified,
		},
	}
	return &response, err
}

func (p Processor) RegisterUser(ctx context.Context, in *api.UserRegistrationRequest, _ ...grpc.CallOption) (*api.UserRegistrationResponse, error) {
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
	d, err := p.deviceService.GetByUserId(ctx, userId)
	if err != nil {
		return nil, err
	}
	if d == nil {
		err := p.addNewDevice(ctx, in.PhoneNumber, userId)
		if err != nil {
			return nil, err
		}
	}
	err = p.nalogClient.AuthByPhone(d)

	return &api.UserRegistrationResponse{UserId: userId}, nil
}

func (p Processor) addNewDevice(ctx context.Context, phoneNumber string, userId string) error {
	d := &nalogDevice.Device{
		ClientSecret: p.clientSecret,
		SessionId:    "",
		RefreshToken: "",
		Update:       nil,
		UserId:       userId,
		Phone:        phoneNumber,
	}
	err := p.deviceService.Add(ctx, d)
	return err
}

// VerifyPhone validate phone number through SMS.
func (p Processor) VerifyPhone(ctx context.Context, req *api.PhoneVerificationRequest) (*api.ErrorResponse, error) {
	user, err := p.repository.GetByTelegramId(ctx, int(req.TelegramId))
	if err != nil {
		return nil, err
	}
	if user == nil {
		response := api.ErrorResponse{
			Error: "User not found",
		}
		return &response, nil
	}
	userId := user.Id.Hex()
	d, err := p.deviceService.GetByUserId(ctx, userId)
	if err != nil {
		return nil, err
	}
	if d == nil {
		response := api.ErrorResponse{
			Error: "Registration process not started",
		}
		return &response, nil
	}

	err = p.nalogClient.VerifyPhone(d, req.Code)
	return &api.ErrorResponse{Error: "TODO"}, err
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
