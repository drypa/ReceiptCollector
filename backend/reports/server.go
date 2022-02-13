package reports

import (
	api "github.com/drypa/ReceiptCollector/api/inside"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials"
	"log"
	"net"
)

type Server struct {
}

//Serve starts Server.
func Serve(bindingAddress string, creds credentials.TransportCredentials) *Server {
	listen, err := net.Listen("tcp", bindingAddress)
	if err != nil {
		log.Printf("Error process address: %s, Error: %v", bindingAddress, err)
		return nil
	}
	opts := []grpc.ServerOption{grpc.Creds(creds)}
	grpcServer := grpc.NewServer(opts...)
	p, err := New()
	if err != nil {
		log.Printf("Filed to create processor %v", err)
		return nil
	}
	api.RegisterReportApiServer(grpcServer, p.s)

	err = grpcServer.Serve(listen)
	if err != nil {
		log.Printf("Filed to serve %v", err)
		return nil
	}

	return &Server{}
}
