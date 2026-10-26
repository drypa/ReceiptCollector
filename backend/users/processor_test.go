package users

import (
	"testing"

	nalogDevice "receipt_collector/nalogru/device"
)

func TestIsDeviceConfirmed(t *testing.T) {
	tests := []struct {
		name     string
		device   *nalogDevice.Device
		expected bool
	}{
		{
			name:     "nil device is not confirmed",
			device:   nil,
			expected: false,
		},
		{
			name:     "device with empty tokens is not confirmed",
			device:   &nalogDevice.Device{SessionId: "", RefreshToken: ""},
			expected: false,
		},
		{
			name:     "non-empty SessionId confirms the device",
			device:   &nalogDevice.Device{SessionId: "session-1", RefreshToken: ""},
			expected: true,
		},
		{
			name:     "non-empty RefreshToken confirms the device",
			device:   &nalogDevice.Device{SessionId: "", RefreshToken: "refresh-1"},
			expected: true,
		},
		{
			name:     "both tokens non-empty confirms the device",
			device:   &nalogDevice.Device{SessionId: "session-1", RefreshToken: "refresh-1"},
			expected: true,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			if got := isDeviceConfirmed(tt.device); got != tt.expected {
				t.Fatalf("isDeviceConfirmed(%+v) = %v, want %v", tt.device, got, tt.expected)
			}
		})
	}
}

func TestConfirmedUserIds(t *testing.T) {
	t.Run("empty list gives empty set", func(t *testing.T) {
		confirmed := confirmedUserIds([]*nalogDevice.Device{})
		if len(confirmed) != 0 {
			t.Fatalf("expected empty set, got %v", confirmed)
		}
	})

	t.Run("only users with a confirmed device are in the set", func(t *testing.T) {
		devices := []*nalogDevice.Device{
			{UserId: "user-confirmed-session", SessionId: "s1", RefreshToken: ""},
			{UserId: "user-confirmed-refresh", SessionId: "", RefreshToken: "r1"},
			{UserId: "user-unconfirmed", SessionId: "", RefreshToken: ""},
		}

		confirmed := confirmedUserIds(devices)

		if !confirmed["user-confirmed-session"] {
			t.Fatalf("user-confirmed-session should be confirmed")
		}
		if !confirmed["user-confirmed-refresh"] {
			t.Fatalf("user-confirmed-refresh should be confirmed")
		}
		if confirmed["user-unconfirmed"] {
			t.Fatalf("user-unconfirmed should not be confirmed")
		}
		if len(confirmed) != 2 {
			t.Fatalf("expected 2 confirmed users, got %d: %v", len(confirmed), confirmed)
		}
	})

	t.Run("several devices per user: any confirmed device is enough", func(t *testing.T) {
		devices := []*nalogDevice.Device{
			{UserId: "multi", SessionId: "", RefreshToken: ""},
			{UserId: "multi", SessionId: "s2", RefreshToken: ""},
		}

		confirmed := confirmedUserIds(devices)

		if !confirmed["multi"] {
			t.Fatalf("user with one confirmed and one unconfirmed device should be confirmed")
		}
		if len(confirmed) != 1 {
			t.Fatalf("expected 1 confirmed user, got %d: %v", len(confirmed), confirmed)
		}
	})

	t.Run("user with only unconfirmed devices is absent", func(t *testing.T) {
		devices := []*nalogDevice.Device{
			{UserId: "multi", SessionId: "", RefreshToken: ""},
			{UserId: "multi", SessionId: "", RefreshToken: ""},
		}

		confirmed := confirmedUserIds(devices)

		if confirmed["multi"] {
			t.Fatalf("user with only unconfirmed devices must not be in the set")
		}
	})
}
