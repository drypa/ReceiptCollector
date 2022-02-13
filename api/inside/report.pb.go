// Code generated by protoc-gen-go. DO NOT EDIT.
// source: report.proto

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

type Report struct {
	Message              string   `protobuf:"bytes,1,opt,name=message,proto3" json:"message,omitempty"`
	UserId               string   `protobuf:"bytes,2,opt,name=userId,proto3" json:"userId,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *Report) Reset()         { *m = Report{} }
func (m *Report) String() string { return proto.CompactTextString(m) }
func (*Report) ProtoMessage()    {}
func (*Report) Descriptor() ([]byte, []int) {
	return fileDescriptor_3eedb623aa6ca98c, []int{0}
}

func (m *Report) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_Report.Unmarshal(m, b)
}
func (m *Report) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_Report.Marshal(b, m, deterministic)
}
func (m *Report) XXX_Merge(src proto.Message) {
	xxx_messageInfo_Report.Merge(m, src)
}
func (m *Report) XXX_Size() int {
	return xxx_messageInfo_Report.Size(m)
}
func (m *Report) XXX_DiscardUnknown() {
	xxx_messageInfo_Report.DiscardUnknown(m)
}

var xxx_messageInfo_Report proto.InternalMessageInfo

func (m *Report) GetMessage() string {
	if m != nil {
		return m.Message
	}
	return ""
}

func (m *Report) GetUserId() string {
	if m != nil {
		return m.UserId
	}
	return ""
}

type NoParams struct {
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *NoParams) Reset()         { *m = NoParams{} }
func (m *NoParams) String() string { return proto.CompactTextString(m) }
func (*NoParams) ProtoMessage()    {}
func (*NoParams) Descriptor() ([]byte, []int) {
	return fileDescriptor_3eedb623aa6ca98c, []int{1}
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

func init() {
	proto.RegisterType((*Report)(nil), "inside_api.Report")
	proto.RegisterType((*NoParams)(nil), "inside_api.NoParams")
}

func init() { proto.RegisterFile("report.proto", fileDescriptor_3eedb623aa6ca98c) }

var fileDescriptor_3eedb623aa6ca98c = []byte{
	// 147 bytes of a gzipped FileDescriptorProto
	0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0xff, 0xe2, 0xe2, 0x29, 0x4a, 0x2d, 0xc8,
	0x2f, 0x2a, 0xd1, 0x2b, 0x28, 0xca, 0x2f, 0xc9, 0x17, 0xe2, 0xca, 0xcc, 0x2b, 0xce, 0x4c, 0x49,
	0x8d, 0x4f, 0x2c, 0xc8, 0x54, 0xb2, 0xe2, 0x62, 0x0b, 0x02, 0xcb, 0x09, 0x49, 0x70, 0xb1, 0xe7,
	0xa6, 0x16, 0x17, 0x27, 0xa6, 0xa7, 0x4a, 0x30, 0x2a, 0x30, 0x6a, 0x70, 0x06, 0xc1, 0xb8, 0x42,
	0x62, 0x5c, 0x6c, 0xa5, 0xc5, 0xa9, 0x45, 0x9e, 0x29, 0x12, 0x4c, 0x60, 0x09, 0x28, 0x4f, 0x89,
	0x8b, 0x8b, 0xc3, 0x2f, 0x3f, 0x20, 0xb1, 0x28, 0x31, 0xb7, 0xd8, 0xc8, 0x9d, 0x8b, 0x13, 0x62,
	0x8e, 0x63, 0x41, 0xa6, 0x90, 0x15, 0x17, 0x97, 0x7b, 0x6a, 0x09, 0x84, 0x5f, 0x2c, 0x24, 0xa2,
	0x87, 0xb0, 0x4f, 0x0f, 0xa6, 0x41, 0x4a, 0x08, 0x59, 0x14, 0xa2, 0x54, 0x89, 0xc1, 0x80, 0x31,
	0x89, 0x0d, 0xec, 0x46, 0x63, 0x40, 0x00, 0x00, 0x00, 0xff, 0xff, 0x03, 0x33, 0xf8, 0xf2, 0xb3,
	0x00, 0x00, 0x00,
}

// Reference imports to suppress errors if they are not otherwise used.
var _ context.Context
var _ grpc.ClientConnInterface

// This is a compile-time assertion to ensure that this generated file
// is compatible with the grpc package it is being compiled against.
const _ = grpc.SupportPackageIsVersion6

// ReportApiClient is the client API for ReportApi service.
//
// For semantics around ctx use and closing/ending streaming RPCs, please refer to https://godoc.org/google.golang.org/grpc#ClientConn.NewStream.
type ReportApiClient interface {
	GetReports(ctx context.Context, in *NoParams, opts ...grpc.CallOption) (ReportApi_GetReportsClient, error)
}

type reportApiClient struct {
	cc grpc.ClientConnInterface
}

func NewReportApiClient(cc grpc.ClientConnInterface) ReportApiClient {
	return &reportApiClient{cc}
}

func (c *reportApiClient) GetReports(ctx context.Context, in *NoParams, opts ...grpc.CallOption) (ReportApi_GetReportsClient, error) {
	stream, err := c.cc.NewStream(ctx, &_ReportApi_serviceDesc.Streams[0], "/inside_api.ReportApi/GetReports", opts...)
	if err != nil {
		return nil, err
	}
	x := &reportApiGetReportsClient{stream}
	if err := x.ClientStream.SendMsg(in); err != nil {
		return nil, err
	}
	if err := x.ClientStream.CloseSend(); err != nil {
		return nil, err
	}
	return x, nil
}

type ReportApi_GetReportsClient interface {
	Recv() (*Report, error)
	grpc.ClientStream
}

type reportApiGetReportsClient struct {
	grpc.ClientStream
}

func (x *reportApiGetReportsClient) Recv() (*Report, error) {
	m := new(Report)
	if err := x.ClientStream.RecvMsg(m); err != nil {
		return nil, err
	}
	return m, nil
}

// ReportApiServer is the server API for ReportApi service.
type ReportApiServer interface {
	GetReports(*NoParams, ReportApi_GetReportsServer) error
}

// UnimplementedReportApiServer can be embedded to have forward compatible implementations.
type UnimplementedReportApiServer struct {
}

func (*UnimplementedReportApiServer) GetReports(req *NoParams, srv ReportApi_GetReportsServer) error {
	return status.Errorf(codes.Unimplemented, "method GetReports not implemented")
}

func RegisterReportApiServer(s *grpc.Server, srv ReportApiServer) {
	s.RegisterService(&_ReportApi_serviceDesc, srv)
}

func _ReportApi_GetReports_Handler(srv interface{}, stream grpc.ServerStream) error {
	m := new(NoParams)
	if err := stream.RecvMsg(m); err != nil {
		return err
	}
	return srv.(ReportApiServer).GetReports(m, &reportApiGetReportsServer{stream})
}

type ReportApi_GetReportsServer interface {
	Send(*Report) error
	grpc.ServerStream
}

type reportApiGetReportsServer struct {
	grpc.ServerStream
}

func (x *reportApiGetReportsServer) Send(m *Report) error {
	return x.ServerStream.SendMsg(m)
}

var _ReportApi_serviceDesc = grpc.ServiceDesc{
	ServiceName: "inside_api.ReportApi",
	HandlerType: (*ReportApiServer)(nil),
	Methods:     []grpc.MethodDesc{},
	Streams: []grpc.StreamDesc{
		{
			StreamName:    "GetReports",
			Handler:       _ReportApi_GetReports_Handler,
			ServerStreams: true,
		},
	},
	Metadata: "report.proto",
}
