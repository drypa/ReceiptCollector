package commands

import (
	"reflect"
	"testing"

	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
)

// fakeSender records everything passed to Send (ADR-029 D8 seam apiSender).
type fakeSender struct {
	sent []tgbotapi.Chattable
	err  error
}

func (f *fakeSender) Send(config tgbotapi.Chattable) (tgbotapi.Message, error) {
	f.sent = append(f.sent, config)
	return tgbotapi.Message{}, f.err
}

func (f *fakeSender) last(t *testing.T) tgbotapi.Chattable {
	t.Helper()
	if len(f.sent) == 0 {
		t.Fatalf("nothing was sent")
	}
	return f.sent[len(f.sent)-1]
}

// fakeAuthorizator is a toggleable Authorizator counting calls (ADR-029 D8).
type fakeAuthorizator struct {
	authorized bool
	calls      int
}

func (f *fakeAuthorizator) IsAuthorized(_ int) bool {
	f.calls++
	return f.authorized
}

func markupOf(t *testing.T, chattable tgbotapi.Chattable) interface{} {
	t.Helper()
	switch c := chattable.(type) {
	case tgbotapi.MessageConfig:
		return c.ReplyMarkup
	case tgbotapi.DocumentConfig:
		return c.ReplyMarkup
	default:
		t.Fatalf("unexpected chattable type %T", chattable)
		return nil
	}
}

func TestMessenger_SendText_AuthorizedGetsKeyboard(t *testing.T) {
	sender := &fakeSender{}
	m := NewMessenger(sender, &fakeAuthorizator{authorized: true}, 100, 200)

	if _, err := m.SendText("hello"); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	sent := sender.last(t).(tgbotapi.MessageConfig)
	if sent.Text != "hello" {
		t.Fatalf("expected text 'hello', got %q", sent.Text)
	}
	if sent.ChatID != 100 {
		t.Fatalf("expected chat id 100, got %d", sent.ChatID)
	}
	if kb, ok := sent.ReplyMarkup.(tgbotapi.ReplyKeyboardMarkup); !ok || !reflect.DeepEqual(kb, BuildLoginKeyboard()) {
		t.Fatalf("authorized user must receive the login keyboard, got %#v", sent.ReplyMarkup)
	}
}

func TestMessenger_SendText_UnauthorizedHasNoKeyboard(t *testing.T) {
	sender := &fakeSender{}
	m := NewMessenger(sender, &fakeAuthorizator{authorized: false}, 100, 200)

	if _, err := m.SendText("hello"); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	sent := sender.last(t).(tgbotapi.MessageConfig)
	if sent.ReplyMarkup != nil {
		t.Fatalf("unauthorized user must get no keyboard, got %#v", sent.ReplyMarkup)
	}
}

func TestMessenger_ReplyText_Authorized(t *testing.T) {
	sender := &fakeSender{}
	m := NewMessenger(sender, &fakeAuthorizator{authorized: true}, 100, 200)

	if _, err := m.ReplyText("sorry", 55); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	sent := sender.last(t).(tgbotapi.MessageConfig)
	if sent.ReplyToMessageID != 55 {
		t.Fatalf("expected reply-to 55, got %d", sent.ReplyToMessageID)
	}
	if kb, ok := sent.ReplyMarkup.(tgbotapi.ReplyKeyboardMarkup); !ok || !reflect.DeepEqual(kb, BuildLoginKeyboard()) {
		t.Fatalf("authorized user must receive the login keyboard, got %#v", sent.ReplyMarkup)
	}
}

func TestMessenger_SendDocument_Authorized(t *testing.T) {
	sender := &fakeSender{}
	m := NewMessenger(sender, &fakeAuthorizator{authorized: true}, 100, 200)

	doc := tgbotapi.NewDocumentUpload(100, tgbotapi.FileBytes{Name: "r.txt", Bytes: []byte("x")})
	if _, err := m.SendDocument(doc); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	sent := sender.last(t).(tgbotapi.DocumentConfig)
	if kb, ok := sent.ReplyMarkup.(tgbotapi.ReplyKeyboardMarkup); !ok || !reflect.DeepEqual(kb, BuildLoginKeyboard()) {
		t.Fatalf("authorized user must receive the login keyboard on documents, got %#v", sent.ReplyMarkup)
	}
}

func TestMessenger_SendDocument_Unauthorized(t *testing.T) {
	sender := &fakeSender{}
	m := NewMessenger(sender, &fakeAuthorizator{authorized: false}, 100, 200)

	doc := tgbotapi.NewDocumentUpload(100, tgbotapi.FileBytes{Name: "r.txt", Bytes: []byte("x")})
	if _, err := m.SendDocument(doc); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	sent := sender.last(t).(tgbotapi.DocumentConfig)
	if sent.ReplyMarkup != nil {
		t.Fatalf("unauthorized user must get no keyboard, got %#v", sent.ReplyMarkup)
	}
}

// TestMessenger_Send_KeepsOwnMarkup verifies Send never rewrites reply_markup:
// the successful /login message carries its inline keyboard and must not get
// the reply keyboard (one reply_markup per message, ADR-029 D5).
func TestMessenger_Send_KeepsOwnMarkup(t *testing.T) {
	sender := &fakeSender{}
	auth := &fakeAuthorizator{authorized: true}
	m := NewMessenger(sender, auth, 100, 200)

	linkMsg := BuildLoginLinkMessage(100, "https://example.com/one-time")
	if _, err := m.Send(linkMsg); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if auth.calls != 0 {
		t.Fatalf("raw Send must not consult the authorizator, got %d calls", auth.calls)
	}
	sent := sender.last(t).(tgbotapi.MessageConfig)
	if _, ok := sent.ReplyMarkup.(tgbotapi.InlineKeyboardMarkup); !ok {
		t.Fatalf("Send must keep the message's own inline markup, got %#v", sent.ReplyMarkup)
	}
}

// TestMessenger_PolicyIsLazy verifies the keyboard policy is evaluated on
// EVERY send, not frozen in the constructor: the same Messenger starts
// unauthorized and, once the user confirms mid-life (the /code scenario),
// the next message already carries the keyboard (ADR-029 D2).
func TestMessenger_PolicyIsLazy(t *testing.T) {
	sender := &fakeSender{}
	auth := &fakeAuthorizator{authorized: false}
	m := NewMessenger(sender, auth, 100, 200)

	if _, err := m.SendText("before confirmation"); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if got := markupOf(t, sender.last(t)); got != nil {
		t.Fatalf("first message must have no keyboard, got %#v", got)
	}

	// The user confirms the phone between the two sends.
	auth.authorized = true

	if _, err := m.SendText("after confirmation"); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	kb, ok := markupOf(t, sender.last(t)).(tgbotapi.ReplyKeyboardMarkup)
	if !ok || !reflect.DeepEqual(kb, BuildLoginKeyboard()) {
		t.Fatalf("message after mid-life confirmation must carry the keyboard, got %#v", sender.last(t))
	}

	// Exactly one authorization check per send (two sends so far).
	if auth.calls != 2 {
		t.Fatalf("expected one IsAuthorized call per send, got %d for 2 sends", auth.calls)
	}
}
