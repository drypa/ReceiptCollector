package commands

import (
	"context"

	"github.com/drypa/ReceiptCollector/bot/analytics"
	tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"
)

// LoginLinkProvider obtains a one-time webUI login link.
// Seam for tests (ADR-029 D8); *analytics.Client satisfies it.
type LoginLinkProvider interface {
	GetLoginLink(ctx context.Context, telegramID int) (string, error)
}

type GetLoginLinkCommand struct {
	analyticsClient LoginLinkProvider
}

func NewGetLoginLinkCommand(analyticsClient *analytics.Client) *GetLoginLinkCommand {
	return &GetLoginLinkCommand{analyticsClient: analyticsClient}
}

// Accepted matches the manual "/login" command and a press of the
// quick-access reply-keyboard button (exact text, ADR-029 D4). The reply
// keyboard press arrives as a plain text message, so the same matching in
// processMessage handles it; no registrar changes are needed.
func (c *GetLoginLinkCommand) Accepted(message string) bool {
	return message == "/login" || message == LoginButtonText
}

// Execute fetches a fresh one-time login link on every call (repeat presses
// simply get a new link — no state).
// Success: message with the inline "Open webUI" URL button sent raw via
// Messenger.Send: Telegram allows exactly one reply_markup per message, so
// this answer carries the inline keyboard and not the reply keyboard
// (ADR-029 D5). Error: plain text through SendText — the reply keyboard is
// attached by the Messenger policy (edge case "link fetch failed, keyboard
// stays").
// The command does not check authorization itself: Analytics remains the only
// source of truth about "may enter webUI" and answers 404 honestly (ADR 003).
func (c *GetLoginLinkCommand) Execute(update tgbotapi.Update, m *Messenger) error {
	ctx, cancel := getContext()
	defer cancel()
	link, err := c.analyticsClient.GetLoginLink(ctx, update.Message.From.ID)
	if err != nil {
		_, sendErr := m.SendText(err.Error())
		return sendErr
	}
	_, sendErr := m.Send(BuildLoginLinkMessage(update.Message.Chat.ID, link))
	return sendErr
}
