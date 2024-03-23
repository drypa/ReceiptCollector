package main

import (
	"context"
	"log"
	"net/http"
	"os"
	"os/signal"
	"time"
)

func main() {
	mux := http.NewServeMux()
	mux.HandleFunc("/v2/mobile/users/refresh", refresh)
	mux.HandleFunc("/v2/auth/phone/verify", verify)
	mux.HandleFunc("/v2/ticket", ticket)

	s := &http.Server{
		Addr:    ":18888",
		Handler: mux,
	}
	go func() {
		err := s.ListenAndServe()
		if err != nil {
			log.Fatal(err)
		}
	}()

	sigChan := make(chan os.Signal)
	signal.Notify(sigChan, os.Kill)
	signal.Notify(sigChan, os.Interrupt)

	sig := <-sigChan
	log.Printf("Service is shutting down... %s\n,", sig)
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	err := s.Shutdown(ctx)

	if err != nil {
		cancel()
		log.Fatal(err)
	}
}

func ticket(writer http.ResponseWriter, request *http.Request) {
	//writer.WriteHeader(http.StatusUnauthorized)
	writeFileToResponse("./ticket.json", writer)
}

func verify(writer http.ResponseWriter, _ *http.Request) {
	writeFileToResponse("./verify.json", writer)
}

func writeFileToResponse(path string, writer http.ResponseWriter) {
	file, err := os.ReadFile(path)
	if err != nil {
		writer.WriteHeader(http.StatusInternalServerError)
		return
	}

	if _, err = writer.Write(file); err != nil {
		writer.WriteHeader(http.StatusInternalServerError)
	}
}

func refresh(writer http.ResponseWriter, _ *http.Request) {
	writeFileToResponse("./refresh.json", writer)
}
