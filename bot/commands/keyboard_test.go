package commands

import (
	"testing"

	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
)

func TestBuildLoginKeyboard(t *testing.T) {
	keyboard := BuildLoginKeyboard()

	if len(keyboard.Keyboard) != 1 {
		t.Fatalf("keyboard must have exactly 1 row, got %d", len(keyboard.Keyboard))
	}
	row := keyboard.Keyboard[0]
	if len(row) != 1 {
		t.Fatalf("row must have exactly 1 button, got %d", len(row))
	}
	if row[0].Text != LoginButtonText {
		t.Fatalf("button text must be %q, got %q", LoginButtonText, row[0].Text)
	}
	if !keyboard.ResizeKeyboard {
		t.Fatalf("ResizeKeyboard must be true")
	}
	if keyboard.OneTimeKeyboard {
		t.Fatalf("OneTimeKeyboard must be false (the keyboard is permanent)")
	}
	if keyboard.Selective {
		t.Fatalf("Selective must be false")
	}
}

func TestBuildLoginLinkMessage(t *testing.T) {
	const link = "https://analytics.example.com/login/one-time-token"

	msg := BuildLoginLinkMessage(12345, link)

	if msg.ChatID != 12345 {
		t.Fatalf("expected chat id 12345, got %d", msg.ChatID)
	}
	if msg.Text == "" {
		t.Fatalf("message must carry a hint text about the link")
	}

	inline, ok := msg.ReplyMarkup.(tgbotapi.InlineKeyboardMarkup)
	if !ok {
		t.Fatalf("reply markup must be tgbotapi.InlineKeyboardMarkup, got %T", msg.ReplyMarkup)
	}
	if len(inline.InlineKeyboard) != 1 {
		t.Fatalf("inline keyboard must have exactly 1 row, got %d", len(inline.InlineKeyboard))
	}
	if len(inline.InlineKeyboard[0]) != 1 {
		t.Fatalf("inline keyboard row must have exactly 1 button, got %d", len(inline.InlineKeyboard[0]))
	}

	button := inline.InlineKeyboard[0][0]
	if button.Text != OpenWebUiButtonText {
		t.Fatalf("button text must be %q, got %q", OpenWebUiButtonText, button.Text)
	}
	if button.URL == nil || *button.URL != link {
		t.Fatalf("button URL must be the login link %q, got %v", link, button.URL)
	}
}
