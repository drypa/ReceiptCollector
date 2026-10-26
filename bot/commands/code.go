package commands

import (
	"context"
	"log"
	"regexp"

	"github.com/drypa/ReceiptCollector/bot/backend"
	"github.com/drypa/ReceiptCollector/bot/backend/user"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
)

// userIdLookup resolves telegramId to userId. Seam for tests (ADR-029 D8);
// *user.Provider satisfies it.
type userIdLookup interface {
	GetUserId(telegramID int) (string, error)
}

// phoneVerifier verifies the phone number with an SMS code. Seam for tests
// (ADR-029 D8); *backend.GrpcClient satisfies it.
type phoneVerifier interface {
	VerifyPhone(ctx context.Context, telegramID int, code string) error
}

// ConfirmationCodeCommand is phone verification command
type ConfirmationCodeCommand struct {
	provider   userIdLookup
	grpcClient phoneVerifier
	regexp     *regexp.Regexp
}

// codeCommandRegexp is the single source of the /code message shape.
func codeCommandRegexp() *regexp.Regexp {
	return regexp.MustCompile(`^/code\s+(?P<code>\d{4})$`)
}

// NewConfirmationCodeCommand creates ConfirmationCodeCommand
func NewConfirmationCodeCommand(provider *user.Provider, grpcClient *backend.GrpcClient) *ConfirmationCodeCommand {
	command := ConfirmationCodeCommand{
		provider:   provider,
		grpcClient: grpcClient,
		regexp:     codeCommandRegexp(),
	}
	return &command
}

func (c ConfirmationCodeCommand) Accepted(message string) bool {
	return c.regexp.MatchString(message)
}

// Execute command.
// The command used to be silent; now it answers with success or error text
// (ADR-029 D6). The quick-access keyboard is applied to THIS message by the
// lazy Messenger policy: the backend writes the device tokens before returning
// from VerifyPhone, so the authorization check at send time is already true
// (read-after-write, ADR-029 D1). No "confirmation marking" state is kept here.
// Success means the absence of a gRPC error: the backend's
// ErrorResponse{Error:"TODO"} is dropped by GrpcClient.VerifyPhone — a known
// backend debt outside this task's scope (ADR-029 D6).
func (c ConfirmationCodeCommand) Execute(update tgbotapi.Update, m *Messenger) error {
	log.Printf("ConfirmationCodeCommand: %s", update.Message.Text)
	userId, err := c.provider.GetUserId(update.Message.From.ID)
	if err != nil {
		return err
	}
	code := c.getCodeFromRequest(update.Message.Text)
	log.Printf("User %s verify phone with code %s\n", userId, code)

	err = c.grpcClient.VerifyPhone(context.Background(), update.Message.From.ID, code)
	responseText := "Your phone is confirmed.\n You can now enter webUI using the button below."
	if err != nil {
		responseText = err.Error()
	}
	_, err = m.ReplyText(responseText, update.Message.MessageID)
	return err
}

func (c ConfirmationCodeCommand) getCodeFromRequest(message string) string {
	matches := c.regexp.FindStringSubmatch(message)
	index := c.regexp.SubexpIndex("code")
	code := matches[index]
	return code
}
