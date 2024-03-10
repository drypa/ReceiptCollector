package logging

import (
	"fmt"
	"net/http"
	"net/http/httputil"
)

// RoundTripper implements the http.RoundTripper interface
type RoundTripper struct {
	Proxied http.RoundTripper
}

func (lrt RoundTripper) RoundTrip(req *http.Request) (res *http.Response, err error) {
	dump, err := httputil.DumpRequestOut(req, true)
	if err != nil {
		return
	}
	fmt.Printf("%s", dump)

	res, err = lrt.Proxied.RoundTrip(req)

	if err != nil {
		fmt.Printf("Error %s %s: %v", req.Method, req.URL.RawQuery, err)
	} else {
		fmt.Printf("Received %v response\n", res.Status)
	}

	return
}
