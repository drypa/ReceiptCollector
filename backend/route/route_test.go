package route

import (
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestPathID(t *testing.T) {
	tests := []struct {
		name      string
		path      string
		paramName string
		expected  string
		ok        bool
	}{
		{name: "valid hex id", path: "/api/receipt/5dc1c9427126cc2841ca384d", paramName: "id", expected: "5dc1c9427126cc2841ca384d", ok: true},
		{name: "valid alphanumeric id", path: "/api/market/someMarket123", paramName: "id", expected: "someMarket123", ok: true},
		{name: "dot is rejected", path: "/api/receipt/a.b", paramName: "id", expected: "", ok: false},
		{name: "slash in segment is not matched at all", path: "/api/receipt/a/b", paramName: "id", expected: "", ok: false},
		{name: "empty id", path: "/api/receipt/", paramName: "id", expected: "", ok: false},
		{name: "cyrillic id is rejected", path: "/api/receipt/чек", paramName: "id", expected: "", ok: false},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			mux := http.NewServeMux()
			var gotID string
			var gotOK bool
			mux.HandleFunc("GET /api/receipt/{id}", func(writer http.ResponseWriter, request *http.Request) {
				gotID, gotOK = PathID(request, test.paramName)
			})
			mux.HandleFunc("GET /api/market/{id}", func(writer http.ResponseWriter, request *http.Request) {
				gotID, gotOK = PathID(request, test.paramName)
			})

			request := httptest.NewRequest(http.MethodGet, test.path, nil)
			mux.ServeHTTP(httptest.NewRecorder(), request)

			if gotID != test.expected || gotOK != test.ok {
				t.Errorf("PathID() = (%q, %v), want (%q, %v)", gotID, gotOK, test.expected, test.ok)
			}
		})
	}
}
