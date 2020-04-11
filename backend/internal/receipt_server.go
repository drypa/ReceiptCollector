package internal

//GRPCReceiptServer is server to use in backend service.
type GRPCReceiptServer struct {
	bindingAddress string
	certFile       string
	keyFile        string
}

//New constructs Server.
func New(bindingAddress string, certFile string, keyFile string) *GRPCReceiptServer {
	return &GRPCReceiptServer{bindingAddress: bindingAddress, certFile: certFile, keyFile: keyFile}
}
