package analytics

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

// UserAuthLinkResponse represents the response from the Analytics API.
type UserAuthLinkResponse struct {
	Link      string    `json:"link"`
	ExpiresAt time.Time `json:"expiresAt"`
}

// Client is an HTTP client for the Analytics API.
type Client struct {
	BaseURL    string
	HTTPClient *http.Client
	MaxRetries int
	RetryDelay time.Duration
}

// NewClient creates a new Analytics API client.
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

// GetLoginLink calls GET /api/users/auth/link?telegramId={telegramId}
// and returns the login link URL.
func (c *Client) GetLoginLink(ctx context.Context, telegramId int) (string, error) {
	endpoint := fmt.Sprintf("%s/api/users/auth/link", c.BaseURL)

	reqURL, err := url.Parse(endpoint)
	if err != nil {
		return "", fmt.Errorf("invalid URL: %w", err)
	}

	q := reqURL.Query()
	q.Set("telegramId", strconv.Itoa(telegramId))
	reqURL.RawQuery = q.Encode()

	var lastErr error
	for attempt := 0; attempt <= c.MaxRetries; attempt++ {
		req, err := http.NewRequestWithContext(ctx, http.MethodGet, reqURL.String(), nil)
		if err != nil {
			return "", fmt.Errorf("creating request: %w", err)
		}

		resp, err := c.HTTPClient.Do(req)
		if err != nil {
			if isRetryableError(err) && attempt < c.MaxRetries {
				time.Sleep(c.RetryDelay * time.Duration(attempt+1))
				lastErr = err
				continue
			}
			return "", fmt.Errorf("request failed: %w", err)
		}

		defer resp.Body.Close()

		switch resp.StatusCode {
		case http.StatusOK:
			body, err := io.ReadAll(resp.Body)
			if err != nil {
				return "", fmt.Errorf("reading response: %w", err)
			}

			var result UserAuthLinkResponse
			if err := json.Unmarshal(body, &result); err != nil {
				return "", fmt.Errorf("decoding response JSON: %w", err)
			}

			return result.Link, nil

		case http.StatusNotFound:
			return "", fmt.Errorf("user with telegramId=%d not found (404)", telegramId)

		case http.StatusBadRequest:
			body, _ := io.ReadAll(resp.Body)
			return "", fmt.Errorf("bad request (400): %s", string(body))

		default:
			body, _ := io.ReadAll(resp.Body)
			return "", fmt.Errorf("unexpected status %d: %s", resp.StatusCode, string(body))
		}
	}

	return "", fmt.Errorf("all retries failed: %w", lastErr)
}

// isRetryableError determines if a request can be retried.
func isRetryableError(err error) bool {
	var netErr net.Error
	if errors.As(err, &netErr) {
		return netErr.Timeout() || netErr.Temporary()
	}
	return false
}
