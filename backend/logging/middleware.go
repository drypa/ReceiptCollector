package logging

import (
	"log"
	"net/http"
	"net/http/httputil"
	"time"
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
	log.Printf("%s", dump)
	start := time.Now()
	res, err = lrt.Proxied.RoundTrip(req)
	elapsed := time.Since(start)
	if err != nil {
		log.Printf("Error %s %s took %s: %v", req.Method, req.URL.RawQuery, elapsed, err)
	} else {
		log.Printf("Received %v in %s\n", res.Status, elapsed)
	}

	return
}
