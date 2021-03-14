package internal

import (
	api "github.com/drypa/ReceiptCollector/api/inside"
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
func Serve(bindingAddress string, creds credentials.TransportCredentials, accountProcessor *AccountProcessor, receiptProcessor *ReceiptProcessor) *GRPCReceiptServer {
	listen, err := net.Listen("tcp", bindingAddress)
	if err != nil {
		log.Printf("Error process address: %s, Error: %v", bindingAddress, err)
		return nil
	}
	opts := []grpc.ServerOption{grpc.Creds(creds)}
	grpcServer := grpc.NewServer(opts...)
	s := newServer(accountProcessor, receiptProcessor)
	api.RegisterInternalApiServer(grpcServer, &s)

	err = grpcServer.Serve(listen)
	if err != nil {
		log.Printf("Filed to serve %v", err)
		return nil
	}

	return &GRPCReceiptServer{}
}
