package user

import (
	"context"
	"errors"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/drypa/ReceiptCollector/bot/backend"
)

// fakeGrpcUserClient is a grpcUserClient test double counting GetUser calls.
type fakeGrpcUserClient struct {
	calls int32
	user  *backend.User
	err   error
	// delay widens the RPC window in the concurrency test so that several
	// goroutines overlap inside the miss path.
	delay time.Duration
}

func (f *fakeGrpcUserClient) GetUser(_ context.Context, _ int) (*backend.User, error) {
	atomic.AddInt32(&f.calls, 1)
	if f.delay > 0 {
		time.Sleep(f.delay)
	}
	return f.user, f.err
}

func (f *fakeGrpcUserClient) getCalls() int {
	return int(atomic.LoadInt32(&f.calls))
}

func newTestProvider(grpc grpcUserClient, entries map[int]entry) *Provider {
	return &Provider{entries: entries, grpc: grpc}
}

func TestIsAuthorized_HitDoesNotCallRPC(t *testing.T) {
	grpc := &fakeGrpcUserClient{}
	provider := newTestProvider(grpc, map[int]entry{
		111: {userId: "user-111", verified: true},
	})

	if !provider.IsAuthorized(111) {
		t.Fatalf("cached confirmed user must be authorized")
	}
	if grpc.getCalls() != 0 {
		t.Fatalf("map hit must not cause an RPC, got %d calls", grpc.getCalls())
	}
}

func TestIsAuthorized_MissVerifiedCachesPositive(t *testing.T) {
	grpc := &fakeGrpcUserClient{user: &backend.User{
		UserId: "user-222", TelegramId: 222, Verified: true,
	}}
	provider := newTestProvider(grpc, map[int]entry{})

	if !provider.IsAuthorized(222) {
		t.Fatalf("verified user must be authorized")
	}
	if grpc.getCalls() != 1 {
		t.Fatalf("miss must cause exactly one RPC, got %d", grpc.getCalls())
	}

	// Second call must be served from cache without another RPC.
	if !provider.IsAuthorized(222) {
		t.Fatalf("verified user must stay authorized")
	}
	if grpc.getCalls() != 1 {
		t.Fatalf("second call must hit the cache, got %d RPC calls", grpc.getCalls())
	}
}

func TestIsAuthorized_MissUnverifiedIsNotCached(t *testing.T) {
	grpc := &fakeGrpcUserClient{user: &backend.User{
		UserId: "user-333", TelegramId: 333, Verified: false,
	}}
	provider := newTestProvider(grpc, map[int]entry{})

	if provider.IsAuthorized(333) {
		t.Fatalf("registered-but-unconfirmed user must not be authorized")
	}
	if provider.IsAuthorized(333) {
		t.Fatalf("registered-but-unconfirmed user must not be authorized")
	}
	// Negative state must not be cached: transitory condition that is about
	// to become true (ADR-029 D1), so every call re-reads: 2 calls so far.
	if grpc.getCalls() != 2 {
		t.Fatalf("unverified state must not be cached, got %d RPC calls, want 2", grpc.getCalls())
	}

	// Once confirmation happens, the next answer comes straight from the RPC
	// (nothing negative was cached to delay the keyboard).
	grpc.user = &backend.User{UserId: "user-333", TelegramId: 333, Verified: true}
	if !provider.IsAuthorized(333) {
		t.Fatalf("user became verified and must be authorized right away")
	}
}

func TestIsAuthorized_NotFoundOrErrorDegradesToFalse(t *testing.T) {
	t.Run("not_found", func(t *testing.T) {
		grpc := &fakeGrpcUserClient{err: errors.New("not_found")}
		provider := newTestProvider(grpc, map[int]entry{})

		if provider.IsAuthorized(999) {
			t.Fatalf("unknown user must not be authorized")
		}
		if len(provider.entries) != 0 {
			t.Fatalf("error must not populate cache, got %v", provider.entries)
		}
	})

	t.Run("grpc error", func(t *testing.T) {
		grpc := &fakeGrpcUserClient{err: errors.New("connection refused")}
		provider := newTestProvider(grpc, map[int]entry{})

		if provider.IsAuthorized(999) {
			t.Fatalf("gRPC failure must degrade to false without panic")
		}
		if len(provider.entries) != 0 {
			t.Fatalf("error must not populate cache, got %v", provider.entries)
		}
	})
}

func TestGetUserId_UnverifiedUserStillResolved(t *testing.T) {
	grpc := &fakeGrpcUserClient{user: &backend.User{
		UserId: "user-444", TelegramId: 444, Verified: false,
	}}
	provider := newTestProvider(grpc, map[int]entry{})

	// Registration flow: /code calls GetUserId BEFORE confirmation.
	userId, err := provider.GetUserId(444)
	if err != nil {
		t.Fatalf("GetUserId for registered unconfirmed user must succeed, got %v", err)
	}
	if userId != "user-444" {
		t.Fatalf("expected user-444, got %s", userId)
	}
	if len(provider.entries) != 0 {
		t.Fatalf("unconfirmed user must not be cached, got %v", provider.entries)
	}

	// And again via RPC because nothing was cached.
	if _, err := provider.GetUserId(444); err != nil {
		t.Fatalf("second GetUserId must still succeed, got %v", err)
	}
	if grpc.getCalls() != 2 {
		t.Fatalf("expected 2 RPC calls, got %d", grpc.getCalls())
	}
}

func TestGetUserId_CachedConfirmedUser(t *testing.T) {
	grpc := &fakeGrpcUserClient{}
	provider := newTestProvider(grpc, map[int]entry{
		555: {userId: "user-555", verified: true},
	})

	userId, err := provider.GetUserId(555)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if userId != "user-555" {
		t.Fatalf("expected user-555, got %s", userId)
	}
	if grpc.getCalls() != 0 {
		t.Fatalf("cache hit must not cause RPC, got %d calls", grpc.getCalls())
	}
}

func TestGetUserId_ErrorPropagated(t *testing.T) {
	grpc := &fakeGrpcUserClient{err: errors.New("not_found")}
	provider := newTestProvider(grpc, map[int]entry{})

	if _, err := provider.GetUserId(777); err == nil {
		t.Fatalf("GetUserId must propagate gRPC error")
	}
}

// TestConcurrentIsAuthorizedAndGetUserId imitates the two goroutines of
// ADR-029 D7 (update processing + processNotifications) hitting the same
// telegramId at once. Run with -race to detect data races.
func TestConcurrentIsAuthorizedAndGetUserId(t *testing.T) {
	grpc := &fakeGrpcUserClient{
		user:  &backend.User{UserId: "user-race", TelegramId: 42, Verified: true},
		delay: time.Millisecond,
	}
	provider := newTestProvider(grpc, map[int]entry{})

	const goroutines = 16
	var wg sync.WaitGroup
	wg.Add(goroutines * 2)
	for i := 0; i < goroutines; i++ {
		go func() {
			defer wg.Done()
			if !provider.IsAuthorized(42) {
				t.Error("verified user must be authorized under concurrency")
			}
		}()
		go func() {
			defer wg.Done()
			if id, err := provider.GetUserId(42); err != nil || id != "user-race" {
				t.Errorf("GetUserId under concurrency: id=%q err=%v", id, err)
			}
		}()
	}
	wg.Wait()

	if _, ok := provider.entries[42]; !ok {
		t.Fatalf("verified user must end up cached")
	}
}
