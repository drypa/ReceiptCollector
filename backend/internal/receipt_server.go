package internal

import (
	"context"
	api "github.com/drypa/ReceiptCollector/api/internal"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials"
	"log"
	"net"
)

//GRPCReceiptServer is server to use in backend service.
type GRPCReceiptServer struct {
	api.UnimplementedInternalApiServer
}

//New constructs Server.
func New(bindingAddress string, creds credentials.TransportCredentials, processor *Processor) *GRPCReceiptServer {
	listen, err := net.Listen("tcp", bindingAddress)
	if err != nil {
		log.Printf("Error process address: %s, Error: %v", bindingAddress, err)
		return nil
	}
	opts := []grpc.ServerOption{grpc.Creds(creds)}
	grpcServer := grpc.NewServer(opts...)
	s := newServer(processor)
	api.RegisterInternalApiServer(grpcServer, &s)
	err = grpcServer.Serve(listen)
	if err != nil {
		log.Printf("Filed to serve %v", err)
		return nil
	}

	return &GRPCReceiptServer{}
}