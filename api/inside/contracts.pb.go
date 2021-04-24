// Code generated by protoc-gen-go. DO NOT EDIT.
// source: contracts.proto

package inside_api

import (
	context "context"
	fmt "fmt"
	proto "github.com/golang/protobuf/proto"
	grpc "google.golang.org/grpc"
	codes "google.golang.org/grpc/codes"
	status "google.golang.org/grpc/status"
	math "math"
)

// Reference imports to suppress errors if they are not otherwise used.
var _ = proto.Marshal
var _ = fmt.Errorf
var _ = math.Inf

// This is a compile-time assertion to ensure that this generated file
// is compatible with the proto package it is being compiled against.
// A compilation error at this line likely means your copy of the
// proto package needs to be updated.
const _ = proto.ProtoPackageIsVersion3 // please upgrade the proto package

type NoParams struct {
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *NoParams) Reset()         { *m = NoParams{} }
func (m *NoParams) String() string { return proto.CompactTextString(m) }
func (*NoParams) ProtoMessage()    {}
func (*NoParams) Descriptor() ([]byte, []int) {
	return fileDescriptor_b6d125f880f9ca35, []int{0}
}

func (m *NoParams) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_NoParams.Unmarshal(m, b)
}
func (m *NoParams) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_NoParams.Marshal(b, m, deterministic)
}
func (m *NoParams) XXX_Merge(src proto.Message) {
	xxx_messageInfo_NoParams.Merge(m, src)
}
func (m *NoParams) XXX_Size() int {
	return xxx_messageInfo_NoParams.Size(m)
}
func (m *NoParams) XXX_DiscardUnknown() {
	xxx_messageInfo_NoParams.DiscardUnknown(m)
}

var xxx_messageInfo_NoParams proto.InternalMessageInfo

type ErrorResponse struct {
	Error                string   `protobuf:"bytes,1,opt,name=error,proto3" json:"error,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *ErrorResponse) Reset()         { *m = ErrorResponse{} }
func (m *ErrorResponse) String() string { return proto.CompactTextString(m) }
func (*ErrorResponse) ProtoMessage()    {}
func (*ErrorResponse) Descriptor() ([]byte, []int) {
	return fileDescriptor_b6d125f880f9ca35, []int{1}
}

func (m *ErrorResponse) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_ErrorResponse.Unmarshal(m, b)
}
func (m *ErrorResponse) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_ErrorResponse.Marshal(b, m, deterministic)
}
func (m *ErrorResponse) XXX_Merge(src proto.Message) {
	xxx_messageInfo_ErrorResponse.Merge(m, src)
}
func (m *ErrorResponse) XXX_Size() int {
	return xxx_messageInfo_ErrorResponse.Size(m)
}
func (m *ErrorResponse) XXX_DiscardUnknown() {
	xxx_messageInfo_ErrorResponse.DiscardUnknown(m)
}

var xxx_messageInfo_ErrorResponse proto.InternalMessageInfo

func (m *ErrorResponse) GetError() string {
	if m != nil {
		return m.Error
	}
	return ""
}

func init() {
	proto.RegisterType((*NoParams)(nil), "inside_api.NoParams")
	proto.RegisterType((*ErrorResponse)(nil), "inside_api.ErrorResponse")
}

func init() { proto.RegisterFile("contracts.proto", fileDescriptor_b6d125f880f9ca35) }

var fileDescriptor_b6d125f880f9ca35 = []byte{
	// 388 bytes of a gzipped FileDescriptorProto
	0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0xff, 0x8c, 0x93, 0x41, 0xaf, 0xd2, 0x40,
	0x10, 0xc7, 0xe1, 0x20, 0xe2, 0x80, 0x68, 0x56, 0x0e, 0x50, 0x15, 0x4d, 0xd5, 0xc4, 0x13, 0x31,
	0xfa, 0x05, 0xc4, 0x28, 0x0d, 0x06, 0x09, 0x16, 0x88, 0x07, 0x0f, 0x66, 0xdd, 0x4e, 0x70, 0x53,
	0xdd, 0xad, 0xbb, 0xd3, 0x03, 0x9f, 0xe7, 0x7d, 0xd1, 0x17, 0xda, 0x2e, 0x6c, 0x09, 0xbc, 0xf7,
	0x8e, 0x33, 0xbf, 0xff, 0xfc, 0xf7, 0x3f, 0x93, 0x16, 0x1e, 0x09, 0xad, 0xc8, 0x70, 0x41, 0x76,
	0x9c, 0x19, 0x4d, 0x9a, 0x81, 0x54, 0x56, 0x26, 0xf8, 0x8b, 0x67, 0x32, 0xe8, 0x19, 0x14, 0x28,
	0x33, 0xc7, 0x82, 0x1e, 0x17, 0x42, 0xe7, 0xea, 0x58, 0x1b, 0xfc, 0x9f, 0xa3, 0x75, 0x75, 0x08,
	0xd0, 0x5e, 0xe8, 0x25, 0x37, 0xfc, 0x9f, 0x0d, 0xdf, 0xc0, 0xc3, 0x2f, 0xc6, 0x68, 0x13, 0xa3,
	0xcd, 0xb4, 0xb2, 0xc8, 0xfa, 0x70, 0x0f, 0xf7, 0x8d, 0x41, 0xf3, 0x65, 0xf3, 0xed, 0x83, 0xb8,
	0x2c, 0xde, 0x5f, 0xb5, 0xa0, 0x33, 0x53, 0x84, 0x46, 0xf1, 0xbf, 0x93, 0x4c, 0xb2, 0x25, 0x74,
	0x23, 0xa4, 0xb9, 0xde, 0x4a, 0x35, 0x97, 0x2a, 0x65, 0x2f, 0xc6, 0xc7, 0x3c, 0x63, 0x9f, 0xc4,
	0xe5, 0xd3, 0xc1, 0x73, 0x5f, 0xe0, 0xd1, 0xf2, 0xd5, 0xb0, 0xc1, 0xbe, 0x01, 0x4c, 0x92, 0x24,
	0x2e, 0x37, 0x61, 0x35, 0xf9, 0xb1, 0xef, 0xdc, 0x46, 0x97, 0xf0, 0xc1, 0xee, 0x23, 0xb4, 0x23,
	0xa4, 0x8d, 0x45, 0x63, 0x59, 0xdf, 0x57, 0xbb, 0xcd, 0x83, 0x67, 0x27, 0x91, 0x0b, 0xad, 0xe7,
	0xf0, 0x19, 0xee, 0x57, 0x5d, 0x16, 0x9c, 0x91, 0xba, 0x28, 0x4f, 0xcf, 0xb2, 0x83, 0xcb, 0x4f,
	0xe8, 0xc6, 0xb8, 0x95, 0x96, 0xd0, 0x14, 0x56, 0xaf, 0x7c, 0x79, 0xa9, 0xdd, 0x53, 0xc3, 0x49,
	0x6a, 0xe5, 0x3c, 0x5f, 0xdf, 0x2c, 0x3a, 0x98, 0x4f, 0xa1, 0x13, 0x21, 0x55, 0xcb, 0x5b, 0x36,
	0x3a, 0x89, 0xe2, 0x80, 0xb3, 0x7d, 0xe2, 0xf3, 0x0a, 0x86, 0x8d, 0x77, 0x4d, 0xb6, 0x80, 0x41,
	0x84, 0x34, 0x95, 0xc6, 0xd2, 0x46, 0x89, 0x14, 0x45, 0x8a, 0x49, 0x35, 0x74, 0xe1, 0x78, 0xc1,
	0x19, 0xab, 0x6a, 0x22, 0x6c, 0xb0, 0x35, 0x3c, 0x5e, 0xa1, 0xab, 0x57, 0xc4, 0x29, 0xb7, 0xf5,
	0xc5, 0x4f, 0xa9, 0x4b, 0x38, 0xf4, 0x45, 0xb5, 0xef, 0xb2, 0x70, 0x1d, 0xba, 0x94, 0x95, 0xfe,
	0x87, 0xa4, 0x3f, 0x95, 0x7d, 0x6d, 0xf2, 0x7b, 0x8e, 0x66, 0xf7, 0x69, 0x57, 0xa2, 0x5b, 0xb2,
	0x7e, 0x85, 0xce, 0x0a, 0x69, 0x2d, 0x45, 0x8a, 0x34, 0x4b, 0xea, 0x37, 0xf4, 0xc0, 0x5d, 0x12,
	0xfe, 0x6e, 0x15, 0xff, 0xd7, 0x87, 0xeb, 0x00, 0x00, 0x00, 0xff, 0xff, 0x2c, 0xd5, 0x47, 0xf8,
	0xae, 0x03, 0x00, 0x00,
}

// Reference imports to suppress errors if they are not otherwise used.
var _ context.Context
var _ grpc.ClientConnInterface

// This is a compile-time assertion to ensure that this generated file
// is compatible with the grpc package it is being compiled against.
const _ = grpc.SupportPackageIsVersion6

// InternalApiClient is the client API for InternalApi service.
//
// For semantics around ctx use and closing/ending streaming RPCs, please refer to https://godoc.org/google.golang.org/grpc#ClientConn.NewStream.
type InternalApiClient interface {
	GetLoginLink(ctx context.Context, in *GetLoginLinkRequest, opts ...grpc.CallOption) (*LoginLinkResponse, error)
	AddReceipt(ctx context.Context, in *AddReceiptRequest, opts ...grpc.CallOption) (*AddReceiptResponse, error)
	GetUsers(ctx context.Context, in *NoParams, opts ...grpc.CallOption) (*GetUsersResponse, error)
	GetUser(ctx context.Context, in *GetUserRequest, opts ...grpc.CallOption) (*GetUserResponse, error)
	RegisterUser(ctx context.Context, in *UserRegistrationRequest, opts ...grpc.CallOption) (*UserRegistrationResponse, error)
	GetReceipts(ctx context.Context, in *GetReceiptsRequest, opts ...grpc.CallOption) (InternalApi_GetReceiptsClient, error)
	GetFirstUnckeckedRequest(ctx context.Context, in *NoParams, opts ...grpc.CallOption) (*ReceiptRequest, error)
	SetRequestStatus(ctx context.Context, in *SetRequestStatusRequest, opts ...grpc.CallOption) (*ErrorResponse, error)
	GetFirstRequestWithStatus(ctx context.Context, in *QueryByStatus, opts ...grpc.CallOption) (*ReceiptRequest, error)
	SetTicketId(ctx context.Context, in *SetTicketIdRequest, opts ...grpc.CallOption) (*ErrorResponse, error)
}

type internalApiClient struct {
	cc grpc.ClientConnInterface
}

func NewInternalApiClient(cc grpc.ClientConnInterface) InternalApiClient {
	return &internalApiClient{cc}
}

func (c *internalApiClient) GetLoginLink(ctx context.Context, in *GetLoginLinkRequest, opts ...grpc.CallOption) (*LoginLinkResponse, error) {
	out := new(LoginLinkResponse)
	err := c.cc.Invoke(ctx, "/inside_api.InternalApi/GetLoginLink", in, out, opts...)
	if err != nil {
		return nil, err
	}
	return out, nil
}

func (c *internalApiClient) AddReceipt(ctx context.Context, in *AddReceiptRequest, opts ...grpc.CallOption) (*AddReceiptResponse, error) {
	out := new(AddReceiptResponse)
	err := c.cc.Invoke(ctx, "/inside_api.InternalApi/AddReceipt", in, out, opts...)
	if err != nil {
		return nil, err
	}
	return out, nil
}

func (c *internalApiClient) GetUsers(ctx context.Context, in *NoParams, opts ...grpc.CallOption) (*GetUsersResponse, error) {
	out := new(GetUsersResponse)
	err := c.cc.Invoke(ctx, "/inside_api.InternalApi/GetUsers", in, out, opts...)
	if err != nil {
		return nil, err
	}
	return out, nil
}

func (c *internalApiClient) GetUser(ctx context.Context, in *GetUserRequest, opts ...grpc.CallOption) (*GetUserResponse, error) {
	out := new(GetUserResponse)
	err := c.cc.Invoke(ctx, "/inside_api.InternalApi/GetUser", in, out, opts...)
	if err != nil {
		return nil, err
	}
	return out, nil
}

func (c *internalApiClient) RegisterUser(ctx context.Context, in *UserRegistrationRequest, opts ...grpc.CallOption) (*UserRegistrationResponse, error) {
	out := new(UserRegistrationResponse)
	err := c.cc.Invoke(ctx, "/inside_api.InternalApi/RegisterUser", in, out, opts...)
	if err != nil {
		return nil, err
	}
	return out, nil
}

func (c *internalApiClient) GetReceipts(ctx context.Context, in *GetReceiptsRequest, opts ...grpc.CallOption) (InternalApi_GetReceiptsClient, error) {
	stream, err := c.cc.NewStream(ctx, &_InternalApi_serviceDesc.Streams[0], "/inside_api.InternalApi/GetReceipts", opts...)
	if err != nil {
		return nil, err
	}
	x := &internalApiGetReceiptsClient{stream}
	if err := x.ClientStream.SendMsg(in); err != nil {
		return nil, err
	}
	if err := x.ClientStream.CloseSend(); err != nil {
		return nil, err
	}
	return x, nil
}

type InternalApi_GetReceiptsClient interface {
	Recv() (*Receipt, error)
	grpc.ClientStream
}

type internalApiGetReceiptsClient struct {
	grpc.ClientStream
}

func (x *internalApiGetReceiptsClient) Recv() (*Receipt, error) {
	m := new(Receipt)
	if err := x.ClientStream.RecvMsg(m); err != nil {
		return nil, err
	}
	return m, nil
}

func (c *internalApiClient) GetFirstUnckeckedRequest(ctx context.Context, in *NoParams, opts ...grpc.CallOption) (*ReceiptRequest, error) {
	out := new(ReceiptRequest)
	err := c.cc.Invoke(ctx, "/inside_api.InternalApi/GetFirstUnckeckedRequest", in, out, opts...)
	if err != nil {
		return nil, err
	}
	return out, nil
}

func (c *internalApiClient) SetRequestStatus(ctx context.Context, in *SetRequestStatusRequest, opts ...grpc.CallOption) (*ErrorResponse, error) {
	out := new(ErrorResponse)
	err := c.cc.Invoke(ctx, "/inside_api.InternalApi/SetRequestStatus", in, out, opts...)
	if err != nil {
		return nil, err
	}
	return out, nil
}

func (c *internalApiClient) GetFirstRequestWithStatus(ctx context.Context, in *QueryByStatus, opts ...grpc.CallOption) (*ReceiptRequest, error) {
	out := new(ReceiptRequest)
	err := c.cc.Invoke(ctx, "/inside_api.InternalApi/GetFirstRequestWithStatus", in, out, opts...)
	if err != nil {
		return nil, err
	}
	return out, nil
}

func (c *internalApiClient) SetTicketId(ctx context.Context, in *SetTicketIdRequest, opts ...grpc.CallOption) (*ErrorResponse, error) {
	out := new(ErrorResponse)
	err := c.cc.Invoke(ctx, "/inside_api.InternalApi/SetTicketId", in, out, opts...)
	if err != nil {
		return nil, err
	}
	return out, nil
}

// InternalApiServer is the server API for InternalApi service.
type InternalApiServer interface {
	GetLoginLink(context.Context, *GetLoginLinkRequest) (*LoginLinkResponse, error)
	AddReceipt(context.Context, *AddReceiptRequest) (*AddReceiptResponse, error)
	GetUsers(context.Context, *NoParams) (*GetUsersResponse, error)
	GetUser(context.Context, *GetUserRequest) (*GetUserResponse, error)
	RegisterUser(context.Context, *UserRegistrationRequest) (*UserRegistrationResponse, error)
	GetReceipts(*GetReceiptsRequest, InternalApi_GetReceiptsServer) error
	GetFirstUnckeckedRequest(context.Context, *NoParams) (*ReceiptRequest, error)
	SetRequestStatus(context.Context, *SetRequestStatusRequest) (*ErrorResponse, error)
	GetFirstRequestWithStatus(context.Context, *QueryByStatus) (*ReceiptRequest, error)
	SetTicketId(context.Context, *SetTicketIdRequest) (*ErrorResponse, error)
}

// UnimplementedInternalApiServer can be embedded to have forward compatible implementations.
type UnimplementedInternalApiServer struct {
}

func (*UnimplementedInternalApiServer) GetLoginLink(ctx context.Context, req *GetLoginLinkRequest) (*LoginLinkResponse, error) {
	return nil, status.Errorf(codes.Unimplemented, "method GetLoginLink not implemented")
}
func (*UnimplementedInternalApiServer) AddReceipt(ctx context.Context, req *AddReceiptRequest) (*AddReceiptResponse, error) {
	return nil, status.Errorf(codes.Unimplemented, "method AddReceipt not implemented")
}
func (*UnimplementedInternalApiServer) GetUsers(ctx context.Context, req *NoParams) (*GetUsersResponse, error) {
	return nil, status.Errorf(codes.Unimplemented, "method GetUsers not implemented")
}
func (*UnimplementedInternalApiServer) GetUser(ctx context.Context, req *GetUserRequest) (*GetUserResponse, error) {
	return nil, status.Errorf(codes.Unimplemented, "method GetUser not implemented")
}
func (*UnimplementedInternalApiServer) RegisterUser(ctx context.Context, req *UserRegistrationRequest) (*UserRegistrationResponse, error) {
	return nil, status.Errorf(codes.Unimplemented, "method RegisterUser not implemented")
}
func (*UnimplementedInternalApiServer) GetReceipts(req *GetReceiptsRequest, srv InternalApi_GetReceiptsServer) error {
	return status.Errorf(codes.Unimplemented, "method GetReceipts not implemented")
}
func (*UnimplementedInternalApiServer) GetFirstUnckeckedRequest(ctx context.Context, req *NoParams) (*ReceiptRequest, error) {
	return nil, status.Errorf(codes.Unimplemented, "method GetFirstUnckeckedRequest not implemented")
}
func (*UnimplementedInternalApiServer) SetRequestStatus(ctx context.Context, req *SetRequestStatusRequest) (*ErrorResponse, error) {
	return nil, status.Errorf(codes.Unimplemented, "method SetRequestStatus not implemented")
}
func (*UnimplementedInternalApiServer) GetFirstRequestWithStatus(ctx context.Context, req *QueryByStatus) (*ReceiptRequest, error) {
	return nil, status.Errorf(codes.Unimplemented, "method GetFirstRequestWithStatus not implemented")
}
func (*UnimplementedInternalApiServer) SetTicketId(ctx context.Context, req *SetTicketIdRequest) (*ErrorResponse, error) {
	return nil, status.Errorf(codes.Unimplemented, "method SetTicketId not implemented")
}

func RegisterInternalApiServer(s *grpc.Server, srv InternalApiServer) {
	s.RegisterService(&_InternalApi_serviceDesc, srv)
}

func _InternalApi_GetLoginLink_Handler(srv interface{}, ctx context.Context, dec func(interface{}) error, interceptor grpc.UnaryServerInterceptor) (interface{}, error) {
	in := new(GetLoginLinkRequest)
	if err := dec(in); err != nil {
		return nil, err
	}
	if interceptor == nil {
		return srv.(InternalApiServer).GetLoginLink(ctx, in)
	}
	info := &grpc.UnaryServerInfo{
		Server:     srv,
		FullMethod: "/inside_api.InternalApi/GetLoginLink",
	}
	handler := func(ctx context.Context, req interface{}) (interface{}, error) {
		return srv.(InternalApiServer).GetLoginLink(ctx, req.(*GetLoginLinkRequest))
	}
	return interceptor(ctx, in, info, handler)
}

func _InternalApi_AddReceipt_Handler(srv interface{}, ctx context.Context, dec func(interface{}) error, interceptor grpc.UnaryServerInterceptor) (interface{}, error) {
	in := new(AddReceiptRequest)
	if err := dec(in); err != nil {
		return nil, err
	}
	if interceptor == nil {
		return srv.(InternalApiServer).AddReceipt(ctx, in)
	}
	info := &grpc.UnaryServerInfo{
		Server:     srv,
		FullMethod: "/inside_api.InternalApi/AddReceipt",
	}
	handler := func(ctx context.Context, req interface{}) (interface{}, error) {
		return srv.(InternalApiServer).AddReceipt(ctx, req.(*AddReceiptRequest))
	}
	return interceptor(ctx, in, info, handler)
}

func _InternalApi_GetUsers_Handler(srv interface{}, ctx context.Context, dec func(interface{}) error, interceptor grpc.UnaryServerInterceptor) (interface{}, error) {
	in := new(NoParams)
	if err := dec(in); err != nil {
		return nil, err
	}
	if interceptor == nil {
		return srv.(InternalApiServer).GetUsers(ctx, in)
	}
	info := &grpc.UnaryServerInfo{
		Server:     srv,
		FullMethod: "/inside_api.InternalApi/GetUsers",
	}
	handler := func(ctx context.Context, req interface{}) (interface{}, error) {
		return srv.(InternalApiServer).GetUsers(ctx, req.(*NoParams))
	}
	return interceptor(ctx, in, info, handler)
}

func _InternalApi_GetUser_Handler(srv interface{}, ctx context.Context, dec func(interface{}) error, interceptor grpc.UnaryServerInterceptor) (interface{}, error) {
	in := new(GetUserRequest)
	if err := dec(in); err != nil {
		return nil, err
	}
	if interceptor == nil {
		return srv.(InternalApiServer).GetUser(ctx, in)
	}
	info := &grpc.UnaryServerInfo{
		Server:     srv,
		FullMethod: "/inside_api.InternalApi/GetUser",
	}
	handler := func(ctx context.Context, req interface{}) (interface{}, error) {
		return srv.(InternalApiServer).GetUser(ctx, req.(*GetUserRequest))
	}
	return interceptor(ctx, in, info, handler)
}

func _InternalApi_RegisterUser_Handler(srv interface{}, ctx context.Context, dec func(interface{}) error, interceptor grpc.UnaryServerInterceptor) (interface{}, error) {
	in := new(UserRegistrationRequest)
	if err := dec(in); err != nil {
		return nil, err
	}
	if interceptor == nil {
		return srv.(InternalApiServer).RegisterUser(ctx, in)
	}
	info := &grpc.UnaryServerInfo{
		Server:     srv,
		FullMethod: "/inside_api.InternalApi/RegisterUser",
	}
	handler := func(ctx context.Context, req interface{}) (interface{}, error) {
		return srv.(InternalApiServer).RegisterUser(ctx, req.(*UserRegistrationRequest))
	}
	return interceptor(ctx, in, info, handler)
}

func _InternalApi_GetReceipts_Handler(srv interface{}, stream grpc.ServerStream) error {
	m := new(GetReceiptsRequest)
	if err := stream.RecvMsg(m); err != nil {
		return err
	}
	return srv.(InternalApiServer).GetReceipts(m, &internalApiGetReceiptsServer{stream})
}

type InternalApi_GetReceiptsServer interface {
	Send(*Receipt) error
	grpc.ServerStream
}

type internalApiGetReceiptsServer struct {
	grpc.ServerStream
}

func (x *internalApiGetReceiptsServer) Send(m *Receipt) error {
	return x.ServerStream.SendMsg(m)
}

func _InternalApi_GetFirstUnckeckedRequest_Handler(srv interface{}, ctx context.Context, dec func(interface{}) error, interceptor grpc.UnaryServerInterceptor) (interface{}, error) {
	in := new(NoParams)
	if err := dec(in); err != nil {
		return nil, err
	}
	if interceptor == nil {
		return srv.(InternalApiServer).GetFirstUnckeckedRequest(ctx, in)
	}
	info := &grpc.UnaryServerInfo{
		Server:     srv,
		FullMethod: "/inside_api.InternalApi/GetFirstUnckeckedRequest",
	}
	handler := func(ctx context.Context, req interface{}) (interface{}, error) {
		return srv.(InternalApiServer).GetFirstUnckeckedRequest(ctx, req.(*NoParams))
	}
	return interceptor(ctx, in, info, handler)
}

func _InternalApi_SetRequestStatus_Handler(srv interface{}, ctx context.Context, dec func(interface{}) error, interceptor grpc.UnaryServerInterceptor) (interface{}, error) {
	in := new(SetRequestStatusRequest)
	if err := dec(in); err != nil {
		return nil, err
	}
	if interceptor == nil {
		return srv.(InternalApiServer).SetRequestStatus(ctx, in)
	}
	info := &grpc.UnaryServerInfo{
		Server:     srv,
		FullMethod: "/inside_api.InternalApi/SetRequestStatus",
	}
	handler := func(ctx context.Context, req interface{}) (interface{}, error) {
		return srv.(InternalApiServer).SetRequestStatus(ctx, req.(*SetRequestStatusRequest))
	}
	return interceptor(ctx, in, info, handler)
}

func _InternalApi_GetFirstRequestWithStatus_Handler(srv interface{}, ctx context.Context, dec func(interface{}) error, interceptor grpc.UnaryServerInterceptor) (interface{}, error) {
	in := new(QueryByStatus)
	if err := dec(in); err != nil {
		return nil, err
	}
	if interceptor == nil {
		return srv.(InternalApiServer).GetFirstRequestWithStatus(ctx, in)
	}
	info := &grpc.UnaryServerInfo{
		Server:     srv,
		FullMethod: "/inside_api.InternalApi/GetFirstRequestWithStatus",
	}
	handler := func(ctx context.Context, req interface{}) (interface{}, error) {
		return srv.(InternalApiServer).GetFirstRequestWithStatus(ctx, req.(*QueryByStatus))
	}
	return interceptor(ctx, in, info, handler)
}

func _InternalApi_SetTicketId_Handler(srv interface{}, ctx context.Context, dec func(interface{}) error, interceptor grpc.UnaryServerInterceptor) (interface{}, error) {
	in := new(SetTicketIdRequest)
	if err := dec(in); err != nil {
		return nil, err
	}
	if interceptor == nil {
		return srv.(InternalApiServer).SetTicketId(ctx, in)
	}
	info := &grpc.UnaryServerInfo{
		Server:     srv,
		FullMethod: "/inside_api.InternalApi/SetTicketId",
	}
	handler := func(ctx context.Context, req interface{}) (interface{}, error) {
		return srv.(InternalApiServer).SetTicketId(ctx, req.(*SetTicketIdRequest))
	}
	return interceptor(ctx, in, info, handler)
}

var _InternalApi_serviceDesc = grpc.ServiceDesc{
	ServiceName: "inside_api.InternalApi",
	HandlerType: (*InternalApiServer)(nil),
	Methods: []grpc.MethodDesc{
		{
			MethodName: "GetLoginLink",
			Handler:    _InternalApi_GetLoginLink_Handler,
		},
		{
			MethodName: "AddReceipt",
			Handler:    _InternalApi_AddReceipt_Handler,
		},
		{
			MethodName: "GetUsers",
			Handler:    _InternalApi_GetUsers_Handler,
		},
		{
			MethodName: "GetUser",
			Handler:    _InternalApi_GetUser_Handler,
		},
		{
			MethodName: "RegisterUser",
			Handler:    _InternalApi_RegisterUser_Handler,
		},
		{
			MethodName: "GetFirstUnckeckedRequest",
			Handler:    _InternalApi_GetFirstUnckeckedRequest_Handler,
		},
		{
			MethodName: "SetRequestStatus",
			Handler:    _InternalApi_SetRequestStatus_Handler,
		},
		{
			MethodName: "GetFirstRequestWithStatus",
			Handler:    _InternalApi_GetFirstRequestWithStatus_Handler,
		},
		{
			MethodName: "SetTicketId",
			Handler:    _InternalApi_SetTicketId_Handler,
		},
	},
	Streams: []grpc.StreamDesc{
		{
			StreamName:    "GetReceipts",
			Handler:       _InternalApi_GetReceipts_Handler,
			ServerStreams: true,
		},
	},
	Metadata: "contracts.proto",
}
