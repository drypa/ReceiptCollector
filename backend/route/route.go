// Package route provides helpers to extract and validate path parameters
// of HTTP requests routed with net/http ServeMux (Go 1.22+ patterns).
package route

import (
	"net/http"
	"regexp"
)

var idPattern = regexp.MustCompile(`^[a-zA-Z0-9]+$`)

// PathID extracts path parameter by name and validates it against the restriction,
// previously expressed with gorilla/mux pattern {id:[a-zA-Z0-9]+}.
// Second return value is false when parameter is missing or invalid;
// handler must respond with 404 in that case.
func PathID(request *http.Request, name string) (string, bool) {
	id := request.PathValue(name)
	if !idPattern.MatchString(id) {
		return "", false
	}
	return id, true
}
