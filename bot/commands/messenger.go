package commands

import tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"

// Authorizator decides whether a telegram user gets the quick-access keyboard.
// Implemented by *user.Provider (ADR-029 D8); fakes are used in tests.
type Authorizator interface {
	IsAuthorized(telegramID int) bool
}

// apiSender is a seam for tests (ADR-029 D8); *tgbotapi.BotAPI satisfies it.
type apiSender interface {
	Send(config tgbotapi.Chattable) (tgbotapi.Message, error)
}

// Messenger sends messages to a single chat applying the keyboard policy:
// every plain text/reply/document message gets the quick-access reply keyboard
// when the user is registered AND phone-confirmed (ADR-029 D1, D2).
type Messenger struct {
	api        apiSender
	auth       Authorizator
	chatID     int64
	telegramID int
}

// NewMessenger constructs a Messenger for one chat. One is built per update
// (and per notification) in bot.go.
func NewMessenger(api apiSender, auth Authorizator, chatID int64, telegramID int) *Messenger {
	return &Messenger{api: api, auth: auth, chatID: chatID, telegramID: telegramID}
}

// Send sends a ready message as is, without touching its reply markup.
// Use it for messages carrying their own markup — the successful /login answer
// with the inline URL button (ADR-029 D5): Telegram allows exactly one
// reply_markup per message.
func (m *Messenger) Send(msg tgbotapi.MessageConfig) (tgbotapi.Message, error) {
	return m.api.Send(msg)
}

// SendText sends a text message, attaching the login keyboard to confirmed users.
func (m *Messenger) SendText(text string) (tgbotapi.Message, error) {
	msg := tgbotapi.NewMessage(m.chatID, text)
	msg.ReplyMarkup = m.markupIfAuthorized()
	return m.api.Send(msg)
}

// ReplyText sends a text message replying to the given message, attaching the
// login keyboard to confirmed users.
func (m *Messenger) ReplyText(text string, replyToMessageID int) (tgbotapi.Message, error) {
	msg := tgbotapi.NewMessage(m.chatID, text)
	msg.ReplyToMessageID = replyToMessageID
	msg.ReplyMarkup = m.markupIfAuthorized()
	return m.api.Send(msg)
}

// SendDocument sends a document, attaching the login keyboard to confirmed
// users. DocumentConfig embeds BaseChat, so ReplyMarkup applies without
// changing the config type.
func (m *Messenger) SendDocument(doc tgbotapi.DocumentConfig) (tgbotapi.Message, error) {
	doc.ReplyMarkup = m.markupIfAuthorized()
	return m.api.Send(doc)
}

// markupIfAuthorized decides AT SEND TIME (lazy, ADR-029 D2) whether to attach
// the keyboard: the user's state can change while an update is processed —
// notably during /code, whose confirmation answer must already carry the
// keyboard. Freezing the decision in the constructor would break that.
func (m *Messenger) markupIfAuthorized() interface{} {
	if m.auth.IsAuthorized(m.telegramID) {
		return BuildLoginKeyboard()
	}
	return nil
}
