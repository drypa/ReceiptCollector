package commands

import (
	"context"
	"errors"
	"reflect"
	"testing"

	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
)

// fakeLoginLinkProvider is a LoginLinkProvider test double (ADR-029 D8).
type fakeLoginLinkProvider struct {
	link  string
	err   error
	calls int
}

func (f *fakeLoginLinkProvider) GetLoginLink(_ context.Context, _ int) (string, error) {
	f.calls++
	return f.link, f.err
}

func loginUpdate() tgbotapi.Update {
	return tgbotapi.Update{
		Message: &tgbotapi.Message{
			MessageID: 7,
			From:      &tgbotapi.User{ID: 200},
			Chat:      &tgbotapi.Chat{ID: 100},
			Text:      "/login",
		},
	}
}

func TestAccepted_GetLoginLinkCommand(t *testing.T) {
	command := &GetLoginLinkCommand{}

	accepted := []string{
		"/login",
		LoginButtonText, // "Войти в webUI" — reply-keyboard press (ADR-029 D4)
	}
	for _, s := range accepted {
		if !command.Accepted(s) {
			t.Fatalf("message %q must be accepted", s)
		}
	}

	rejected := []string{
		"/login ",           // trailing spaces are trimmed by processMessage beforehand
		"войти в webui",     // different case
		" Войти в webUI ",   // untrimmed button text is never matched (TrimSpace happens earlier)
		OpenWebUiButtonText, // that is the inline button text, not a command
		"",
		"login",
		"some random text",
	}
	for _, s := range rejected {
		if command.Accepted(s) {
			t.Fatalf("message %q must not be accepted", s)
		}
	}
}

func TestExecute_LoginLink_Success_SendsInlineButtonWithoutReplyKeyboard(t *testing.T) {
	const link = "https://analytics.example.com/login/once"
	sender := &fakeSender{}
	m := NewMessenger(sender, &fakeAuthorizator{authorized: true}, 100, 200)
	command := &GetLoginLinkCommand{analyticsClient: &fakeLoginLinkProvider{link: link}}

	if err := command.Execute(loginUpdate(), m); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	sent := sender.last(t).(tgbotapi.MessageConfig)
	inline, ok := sent.ReplyMarkup.(tgbotapi.InlineKeyboardMarkup)
	if !ok {
		t.Fatalf("success answer must carry an inline keyboard, got %#v", sent.ReplyMarkup)
	}
	button := inline.InlineKeyboard[0][0]
	if button.Text != OpenWebUiButtonText {
		t.Fatalf("inline button text must be %q, got %q", OpenWebUiButtonText, button.Text)
	}
	if button.URL == nil || *button.URL != link {
		t.Fatalf("inline button must open %q, got %v", link, button.URL)
	}
	// Exactly one reply_markup per message: no reply keyboard on this answer.
	if _, isReply := sent.ReplyMarkup.(tgbotapi.ReplyKeyboardMarkup); isReply {
		t.Fatalf("success answer must not carry the reply keyboard")
	}
}

func TestExecute_LoginLink_Failure_KeepsReplyKeyboardForAuthorized(t *testing.T) {
	linkErr := errors.New("user with telegramId=200 not found (404)")
	sender := &fakeSender{}
	m := NewMessenger(sender, &fakeAuthorizator{authorized: true}, 100, 200)
	command := &GetLoginLinkCommand{analyticsClient: &fakeLoginLinkProvider{err: linkErr}}

	if err := command.Execute(loginUpdate(), m); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	sent := sender.last(t).(tgbotapi.MessageConfig)
	if sent.Text != linkErr.Error() {
		t.Fatalf("expected error text %q, got %q", linkErr.Error(), sent.Text)
	}
	if kb, ok := sent.ReplyMarkup.(tgbotapi.ReplyKeyboardMarkup); !ok || !reflect.DeepEqual(kb, BuildLoginKeyboard()) {
		t.Fatalf("authorized user keeps the keyboard on errors, got %#v", sent.ReplyMarkup)
	}
}

func TestExecute_LoginLink_Failure_NoKeyboardForUnauthorized(t *testing.T) {
	linkErr := errors.New("user with telegramId=200 not found (404)")
	sender := &fakeSender{}
	m := NewMessenger(sender, &fakeAuthorizator{authorized: false}, 100, 200)
	command := &GetLoginLinkCommand{analyticsClient: &fakeLoginLinkProvider{err: linkErr}}

	if err := command.Execute(loginUpdate(), m); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	sent := sender.last(t).(tgbotapi.MessageConfig)
	if sent.Text != linkErr.Error() {
		t.Fatalf("expected error text %q, got %q", linkErr.Error(), sent.Text)
	}
	if sent.ReplyMarkup != nil {
		t.Fatalf("unauthorized user must get no keyboard, got %#v", sent.ReplyMarkup)
	}
}

// TestExecute_LoginLink_RepeatPressGetsNewLink verifies statelessness: every
// press re-requests the link (edge case of the task: repeat presses are fine,
// each gets a fresh one-time link).
func TestExecute_LoginLink_RepeatPressGetsNewLink(t *testing.T) {
	sender := &fakeSender{}
	m := NewMessenger(sender, &fakeAuthorizator{authorized: true}, 100, 200)
	provider := &fakeLoginLinkProvider{link: "https://example.com/one-time"}
	command := &GetLoginLinkCommand{analyticsClient: provider}

	if err := command.Execute(loginUpdate(), m); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if err := command.Execute(loginUpdate(), m); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if provider.calls != 2 {
		t.Fatalf("every press must fetch a new link, got %d provider calls", provider.calls)
	}
	if len(sender.sent) != 2 {
		t.Fatalf("expected 2 sent messages, got %d", len(sender.sent))
	}
}
