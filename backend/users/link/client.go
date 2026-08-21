package link

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/url"
	"strconv"
	"time"
)

type UserAuthLinkResponse struct {
	Link      *string   `json:"link,omitempty"`
	ExpiresAt time.Time `json:"expiresAt"`
}

// Client конфигурация HTTP-клиента
type Client struct {
	BaseURL    string
	HTTPClient *http.Client
	MaxRetries int
	RetryDelay time.Duration
}

// NewClient создаёт новый экземпляр клиента
func NewClient(baseURL string) *Client {
	return &Client{
		BaseURL: baseURL,
		HTTPClient: &http.Client{
			Timeout: 10 * time.Second,
		},
		MaxRetries: 3,
		RetryDelay: 1 * time.Second,
	}
}

// GetUserAuthLink выполняет GET /api/users/auth/link?telegramId=...
func (c *Client) GetUserAuthLink(ctx context.Context, telegramID int) (*UserAuthLinkResponse, error) {
	endpoint := fmt.Sprintf("%s/api/users/auth/link", c.BaseURL)

	reqURL, err := url.Parse(endpoint)
	if err != nil {
		return nil, fmt.Errorf("invalid URL: %w", err)
	}

	q := reqURL.Query()
	q.Set("telegramId", strconv.Itoa(telegramID))
	reqURL.RawQuery = q.Encode()

	var lastErr error
	for attempt := 0; attempt <= c.MaxRetries; attempt++ {
		req, err := http.NewRequestWithContext(ctx, http.MethodGet, reqURL.String(), nil)
		if err != nil {
			return nil, fmt.Errorf("creating request: %w", err)
		}

		resp, err := c.HTTPClient.Do(req)
		if err != nil {
			if isRetryableError(err) && attempt < c.MaxRetries {
				time.Sleep(c.RetryDelay * time.Duration(attempt+1))
				lastErr = err
				continue
			}
			return nil, fmt.Errorf("request failed: %w", err)
		}

		return func() (*UserAuthLinkResponse, error) {
			defer resp.Body.Close()
			switch resp.StatusCode {
			case http.StatusOK:
				body, err := io.ReadAll(resp.Body)
				if err != nil {
					return nil, fmt.Errorf("reading response: %w", err)
				}

				var result UserAuthLinkResponse
				if err := json.Unmarshal(body, &result); err != nil {
					return nil, fmt.Errorf("decoding response JSON: %w", err)
				}

				return &result, nil

			case http.StatusNoContent:
				// 204 — данных нет
				return nil, nil

			case http.StatusNotFound:
				return nil, fmt.Errorf("user with telegramId=%d not found (404)", telegramID)

			default:
				body, _ := io.ReadAll(resp.Body)
				return nil, fmt.Errorf("unexpected status %d: %s", resp.StatusCode, string(body))
			}
		}()
	}

	return nil, fmt.Errorf("all retries failed: %w", lastErr)
}

// isRetryableError определяет, можно ли повторить запрос
func isRetryableError(err error) bool {
	var netErr net.Error
	if errors.As(err, &netErr) {
		return netErr.Timeout() || netErr.Temporary()
	}
	return false
}
