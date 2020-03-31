package login_url

import (
	"crypto/sha256"
	"encoding/base64"
	"fmt"
	"github.com/google/uuid"
)

//Generator creates unique link for user.
type Generator struct {
	baseAddress string
}

//New creates Generator.
func New(baseAddress string) *Generator {
	return &Generator{baseAddress: baseAddress}
}

//GetRedirectLink returns unique link.
func (generator *Generator) GetRedirectLink(userId string) (string, error) {
	u := uuid.New().String()
	hash := getHashOf(u)
	url := fmt.Sprintf("%s/api/auth/link/%s?u=%s&h=%s", generator.baseAddress, userId, u, hash)
	return url, nil
}

func getHashOf(val string) string {
	hash := sha256.New()
	hash.Write([]byte(val))
	return base64.URLEncoding.EncodeToString(hash.Sum(nil))
}
