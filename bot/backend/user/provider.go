package user

import (
	"context"
	"sync"

	"github.com/drypa/ReceiptCollector/bot/backend"
)

// grpcUserClient is the gRPC surface the Provider needs.
// It is a seam for tests (ADR-029 D8); *backend.GrpcClient satisfies it.
type grpcUserClient interface {
	GetUser(ctx context.Context, telegramID int) (*backend.User, error)
}

// entry is a cached record about a confirmed telegram user.
type entry struct {
	userId   string
	verified bool
}

// Provider provides telegramId to userId mapping and reports whether a user is
// registered AND phone-confirmed (ADR-029 D1).
// Only confirmed users (verified == true) live in the map: negative states
// ("not confirmed" / "not found" / gRPC error) are transitory and are re-read
// on every call so the quick-access keyboard appears as soon as confirmation
// happens.
// The Provider is read and written from two goroutines (update processing and
// processNotifications, ADR-029 D7), so all map access is guarded by mu.
type Provider struct {
	mu      sync.RWMutex
	entries map[int]entry
	grpc    grpcUserClient
}

// New constructs Provider instance pre-populated with confirmed users.
func New(grpc *backend.GrpcClient) (Provider, error) {
	users, err := grpc.GetUsers(context.Background())
	if err != nil {
		return Provider{}, err
	}
	entries := make(map[int]entry)
	for _, u := range users {
		if u.Verified {
			entries[u.TelegramId] = entry{userId: u.UserId, verified: true}
		}
	}
	return Provider{entries: entries, grpc: grpc}, nil
}

// GetUserId returns userId by telegramId.
// It keeps the "existence" semantics: it succeeds for registered-but-not-yet
// confirmed users too (the registration flow calls it from /code before
// confirmation). Only confirmed users are cached.
func (provider *Provider) GetUserId(telegramId int) (string, error) {
	provider.mu.RLock()
	e, ok := provider.entries[telegramId]
	provider.mu.RUnlock()
	if ok {
		return e.userId, nil
	}
	user, err := provider.grpc.GetUser(context.Background(), telegramId) //TODO: pass correct context
	if err != nil {
		return "", err
	}
	if user.Verified {
		provider.cache(telegramId, user.UserId)
	}
	return user.UserId, nil
}

// IsAuthorized reports whether the user is registered AND phone-confirmed.
// A map hit means true without an RPC; on a miss the flag is taken from
// backend GetUser and only a positive answer is cached. Any gRPC error
// degrades to false without interrupting message processing (ADR-029 D1).
func (provider *Provider) IsAuthorized(telegramID int) bool {
	provider.mu.RLock()
	_, ok := provider.entries[telegramID]
	provider.mu.RUnlock()
	if ok {
		return true
	}
	// The lock is deliberately not held during the RPC (ADR-029 D1, BOT-2).
	user, err := provider.grpc.GetUser(context.Background(), telegramID)
	if err != nil || !user.Verified {
		return false
	}
	provider.cache(telegramID, user.UserId)
	return true
}

// cache stores a confirmed user under write-lock; negative states are never cached.
func (provider *Provider) cache(telegramID int, userId string) {
	provider.mu.Lock()
	defer provider.mu.Unlock()
	if provider.entries == nil {
		provider.entries = make(map[int]entry)
	}
	provider.entries[telegramID] = entry{userId: userId, verified: true}
}
