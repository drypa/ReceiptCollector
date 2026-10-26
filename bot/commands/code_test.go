package commands

import (
	"context"
	"errors"
	"reflect"
	"testing"

	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
)

// fakeUserIdLookup is a userIdLookup test double (ADR-029 D8 seam).
type fakeUserIdLookup struct {
	userId string
	err    error
}

func (f *fakeUserIdLookup) GetUserId(_ int) (string, error) {
	return f.userId, f.err
}

// fakePhoneVerifier is a phoneVerifier test double (ADR-029 D8 seam).
type fakePhoneVerifier struct {
	err            error
	calls          int
	lastTelegramId int
	lastCode       string
}

func (f *fakePhoneVerifier) VerifyPhone(_ context.Context, telegramID int, code string) error {
	f.calls++
	f.lastTelegramId = telegramID
	f.lastCode = code
	return f.err
}

func codeUpdate() tgbotapi.Update {
	return tgbotapi.Update{
		Message: &tgbotapi.Message{
			MessageID: 21,
			From:      &tgbotapi.User{ID: 200},
			Chat:      &tgbotapi.Chat{ID: 100},
			Text:      "/code 1234",
		},
	}
}

func newTestCodeCommand(provider userIdLookup, verifier phoneVerifier) ConfirmationCodeCommand {
	return ConfirmationCodeCommand{
		provider:   provider,
		grpcClient: verifier,
		regexp:     codeCommandRegexp(),
	}
}

func TestExecute_ConfirmationCode_Success_AnswersWithKeyboardOnceConfirmed(t *testing.T) {
	sender := &fakeSender{}
	// At send time the user is already confirmed: the backend wrote the device
	// tokens before returning from VerifyPhone, and the Messenger evaluates the
	// policy lazily (ADR-029 D1/D2) — so THIS answer carries the keyboard.
	auth := &fakeAuthorizator{authorized: true}
	m := NewMessenger(sender, auth, 100, 200)
	verifier := &fakePhoneVerifier{}
	command := newTestCodeCommand(&fakeUserIdLookup{userId: "user-200"}, verifier)

	if err := command.Execute(codeUpdate(), m); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if verifier.calls != 1 || verifier.lastCode != "1234" || verifier.lastTelegramId != 200 {
		t.Fatalf("VerifyPhone was not called correctly: calls=%d code=%q telegramId=%d",
			verifier.calls, verifier.lastCode, verifier.lastTelegramId)
	}

	sent := sender.last(t).(tgbotapi.MessageConfig)
	if sent.Text == "" {
		t.Fatalf("the command must answer with a confirmation message (it used to be silent)")
	}
	if sent.ReplyToMessageID != 21 {
		t.Fatalf("answer must reply to the /code message, got reply-to %d", sent.ReplyToMessageID)
	}
	if kb, ok := sent.ReplyMarkup.(tgbotapi.ReplyKeyboardMarkup); !ok || !reflect.DeepEqual(kb, BuildLoginKeyboard()) {
		t.Fatalf("confirmation answer must carry the login keyboard, got %#v", sent.ReplyMarkup)
	}
}

func TestExecute_ConfirmationCode_Failure_AnswersWithErrorText(t *testing.T) {
	verifyErr := errors.New("invalid code")
	sender := &fakeSender{}
	// Typical case: on failure the user is still not confirmed → no keyboard.
	m := NewMessenger(sender, &fakeAuthorizator{authorized: false}, 100, 200)
	verifier := &fakePhoneVerifier{err: verifyErr}
	command := newTestCodeCommand(&fakeUserIdLookup{userId: "user-200"}, verifier)

	if err := command.Execute(codeUpdate(), m); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	sent := sender.last(t).(tgbotapi.MessageConfig)
	if sent.Text != verifyErr.Error() {
		t.Fatalf("expected error text %q, got %q", verifyErr.Error(), sent.Text)
	}
	if sent.ReplyMarkup != nil {
		t.Fatalf("unconfirmed user must get no keyboard, got %#v", sent.ReplyMarkup)
	}
}

func TestExecute_ConfirmationCode_UserLookupFailure_SendsNothing(t *testing.T) {
	sender := &fakeSender{}
	m := NewMessenger(sender, &fakeAuthorizator{authorized: false}, 100, 200)
	verifier := &fakePhoneVerifier{}
	command := newTestCodeCommand(&fakeUserIdLookup{err: errors.New("not_found")}, verifier)

	if err := command.Execute(codeUpdate(), m); err == nil {
		t.Fatalf("user lookup failure must be returned")
	}
	if len(sender.sent) != 0 {
		t.Fatalf("nothing must be sent when the user cannot be resolved, got %d messages", len(sender.sent))
	}
	if verifier.calls != 0 {
		t.Fatalf("VerifyPhone must not be called when the user cannot be resolved")
	}
}

func TestAccepted_ConfirmationCodeCommand_Success(t *testing.T) {
	command := NewConfirmationCodeCommand(nil, nil)

	variants := make([]string, 0)
	variants = append(variants, "/code 1234")
	variants = append(variants, "/code	9876")
	variants = append(variants, "/code    0000")
	variants = append(variants, "/code 	 0001")

	for _, s := range variants {
		res := command.Accepted(s)
		if !res {
			t.Fatalf("message '%s' is not acceptable for ConfirmationCodeCommand", s)
		}
	}
}

func TestAccepted_ConfirmationCodeCommand_Failed(t *testing.T) {
	command := NewConfirmationCodeCommand(nil, nil)

	variants := make([]string, 0)
	variants = append(variants, "")
	variants = append(variants, " ")
	variants = append(variants, "	")
	variants = append(variants, "/code")
	variants = append(variants, "code 1234")
	variants = append(variants, "/code1234")
	variants = append(variants, "	/code 1234 ")
	variants = append(variants, "/code +")
	variants = append(variants, "/code ++++")
	variants = append(variants, "/code dddd")
	variants = append(variants, "/code 1")
	variants = append(variants, "/code 12")
	variants = append(variants, "/code 123")
	variants = append(variants, "/code 12345")
	variants = append(variants, "/code +.*")

	for _, s := range variants {
		res := command.Accepted(s)
		if res {
			t.Fatalf("message '%s' is accepted for RegisterCommand", s)
		}
	}
}

func Test_getCodeFromRequest_Success(t *testing.T) {
	command := NewConfirmationCodeCommand(nil, nil)

	message := "/code 0101"

	phone := command.getCodeFromRequest(message)
	expected := "0101"
	if phone != expected {
		t.Fatalf("wrong code returned from getCodeFromRequest. Expeced '%s' but got '%s'", expected, phone)
	}
}
