package commands

import tgbotapi "github.com/go-telegram-bot-api/telegram-bot-api"

// Button texts live here and nowhere else (ADR-029 D3).
const (
	// LoginButtonText is the reply-keyboard button; pressing it is handled
	// exactly like the /login command (ADR-029 D4).
	LoginButtonText = "Войти в webUI"
	// OpenWebUiButtonText is the inline URL button of the successful /login answer.
	OpenWebUiButtonText = "Открыть webUI"
)

// BuildLoginKeyboard builds the quick-access reply keyboard: one row with one
// button. tgbotapi v4.6.4 has no `is_persistent` field, so "persistent" is
// achieved by attaching this markup to every answer to a confirmed user
// (exactly the task requirement, ADR-029 D3).
// Pure function: no network, cheap to build per send.
func BuildLoginKeyboard() tgbotapi.ReplyKeyboardMarkup {
	return tgbotapi.ReplyKeyboardMarkup{
		Keyboard: [][]tgbotapi.KeyboardButton{
			{tgbotapi.NewKeyboardButton(LoginButtonText)},
		},
		ResizeKeyboard:  true,
		OneTimeKeyboard: false,
		Selective:       false,
	}
}

// BuildLoginLinkMessage builds the successful /login answer: a hint about the
// one-time link plus a single inline URL button leading to it. The message
// carries its own reply_markup (inline keyboard), so the reply keyboard cannot
// be attached to it — Telegram allows exactly one reply_markup per message
// (ADR-029 D5).
func BuildLoginLinkMessage(chatID int64, link string) tgbotapi.MessageConfig {
	msg := tgbotapi.NewMessage(chatID,
		"Your one-time link to the webUI.\nIt is valid for a short time only — open it now using the button below.")
	msg.ReplyMarkup = tgbotapi.InlineKeyboardMarkup{
		InlineKeyboard: [][]tgbotapi.InlineKeyboardButton{
			{tgbotapi.NewInlineKeyboardButtonURL(OpenWebUiButtonText, link)},
		},
	}
	return msg
}
