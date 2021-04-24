// Code generated by protoc-gen-go. DO NOT EDIT.
// source: requests.proto

package inside_api

import (
	fmt "fmt"
	proto "github.com/golang/protobuf/proto"
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

type Status int32

const (
	Status_undefined   Status = 0
	Status_checkPassed Status = 1
	Status_checkFailed Status = 2
	Status_requested   Status = 3
	Status_error       Status = 4
	Status_notFound    Status = 5
)

var Status_name = map[int32]string{
	0: "undefined",
	1: "checkPassed",
	2: "checkFailed",
	3: "requested",
	4: "error",
	5: "notFound",
}

var Status_value = map[string]int32{
	"undefined":   0,
	"checkPassed": 1,
	"checkFailed": 2,
	"requested":   3,
	"error":       4,
	"notFound":    5,
}

func (x Status) String() string {
	return proto.EnumName(Status_name, int32(x))
}

func (Status) EnumDescriptor() ([]byte, []int) {
	return fileDescriptor_9c9ccec99da7c9b4, []int{0}
}

type ReceiptRequest struct {
	Id                   string   `protobuf:"bytes,1,opt,name=id,proto3" json:"id,omitempty"`
	UserId               string   `protobuf:"bytes,2,opt,name=userId,proto3" json:"userId,omitempty"`
	Qr                   string   `protobuf:"bytes,3,opt,name=qr,proto3" json:"qr,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *ReceiptRequest) Reset()         { *m = ReceiptRequest{} }
func (m *ReceiptRequest) String() string { return proto.CompactTextString(m) }
func (*ReceiptRequest) ProtoMessage()    {}
func (*ReceiptRequest) Descriptor() ([]byte, []int) {
	return fileDescriptor_9c9ccec99da7c9b4, []int{0}
}

func (m *ReceiptRequest) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_ReceiptRequest.Unmarshal(m, b)
}
func (m *ReceiptRequest) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_ReceiptRequest.Marshal(b, m, deterministic)
}
func (m *ReceiptRequest) XXX_Merge(src proto.Message) {
	xxx_messageInfo_ReceiptRequest.Merge(m, src)
}
func (m *ReceiptRequest) XXX_Size() int {
	return xxx_messageInfo_ReceiptRequest.Size(m)
}
func (m *ReceiptRequest) XXX_DiscardUnknown() {
	xxx_messageInfo_ReceiptRequest.DiscardUnknown(m)
}

var xxx_messageInfo_ReceiptRequest proto.InternalMessageInfo

func (m *ReceiptRequest) GetId() string {
	if m != nil {
		return m.Id
	}
	return ""
}

func (m *ReceiptRequest) GetUserId() string {
	if m != nil {
		return m.UserId
	}
	return ""
}

func (m *ReceiptRequest) GetQr() string {
	if m != nil {
		return m.Qr
	}
	return ""
}

type SetRequestStatusRequest struct {
	Id                   string   `protobuf:"bytes,1,opt,name=id,proto3" json:"id,omitempty"`
	Status               Status   `protobuf:"varint,2,opt,name=status,proto3,enum=inside_api.Status" json:"status,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *SetRequestStatusRequest) Reset()         { *m = SetRequestStatusRequest{} }
func (m *SetRequestStatusRequest) String() string { return proto.CompactTextString(m) }
func (*SetRequestStatusRequest) ProtoMessage()    {}
func (*SetRequestStatusRequest) Descriptor() ([]byte, []int) {
	return fileDescriptor_9c9ccec99da7c9b4, []int{1}
}

func (m *SetRequestStatusRequest) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_SetRequestStatusRequest.Unmarshal(m, b)
}
func (m *SetRequestStatusRequest) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_SetRequestStatusRequest.Marshal(b, m, deterministic)
}
func (m *SetRequestStatusRequest) XXX_Merge(src proto.Message) {
	xxx_messageInfo_SetRequestStatusRequest.Merge(m, src)
}
func (m *SetRequestStatusRequest) XXX_Size() int {
	return xxx_messageInfo_SetRequestStatusRequest.Size(m)
}
func (m *SetRequestStatusRequest) XXX_DiscardUnknown() {
	xxx_messageInfo_SetRequestStatusRequest.DiscardUnknown(m)
}

var xxx_messageInfo_SetRequestStatusRequest proto.InternalMessageInfo

func (m *SetRequestStatusRequest) GetId() string {
	if m != nil {
		return m.Id
	}
	return ""
}

func (m *SetRequestStatusRequest) GetStatus() Status {
	if m != nil {
		return m.Status
	}
	return Status_undefined
}

type QueryByStatus struct {
	Status               Status   `protobuf:"varint,1,opt,name=status,proto3,enum=inside_api.Status" json:"status,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *QueryByStatus) Reset()         { *m = QueryByStatus{} }
func (m *QueryByStatus) String() string { return proto.CompactTextString(m) }
func (*QueryByStatus) ProtoMessage()    {}
func (*QueryByStatus) Descriptor() ([]byte, []int) {
	return fileDescriptor_9c9ccec99da7c9b4, []int{2}
}

func (m *QueryByStatus) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_QueryByStatus.Unmarshal(m, b)
}
func (m *QueryByStatus) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_QueryByStatus.Marshal(b, m, deterministic)
}
func (m *QueryByStatus) XXX_Merge(src proto.Message) {
	xxx_messageInfo_QueryByStatus.Merge(m, src)
}
func (m *QueryByStatus) XXX_Size() int {
	return xxx_messageInfo_QueryByStatus.Size(m)
}
func (m *QueryByStatus) XXX_DiscardUnknown() {
	xxx_messageInfo_QueryByStatus.DiscardUnknown(m)
}

var xxx_messageInfo_QueryByStatus proto.InternalMessageInfo

func (m *QueryByStatus) GetStatus() Status {
	if m != nil {
		return m.Status
	}
	return Status_undefined
}

type SetTicketIdRequest struct {
	Id                   string   `protobuf:"bytes,1,opt,name=id,proto3" json:"id,omitempty"`
	TicketId             string   `protobuf:"bytes,2,opt,name=ticketId,proto3" json:"ticketId,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *SetTicketIdRequest) Reset()         { *m = SetTicketIdRequest{} }
func (m *SetTicketIdRequest) String() string { return proto.CompactTextString(m) }
func (*SetTicketIdRequest) ProtoMessage()    {}
func (*SetTicketIdRequest) Descriptor() ([]byte, []int) {
	return fileDescriptor_9c9ccec99da7c9b4, []int{3}
}

func (m *SetTicketIdRequest) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_SetTicketIdRequest.Unmarshal(m, b)
}
func (m *SetTicketIdRequest) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_SetTicketIdRequest.Marshal(b, m, deterministic)
}
func (m *SetTicketIdRequest) XXX_Merge(src proto.Message) {
	xxx_messageInfo_SetTicketIdRequest.Merge(m, src)
}
func (m *SetTicketIdRequest) XXX_Size() int {
	return xxx_messageInfo_SetTicketIdRequest.Size(m)
}
func (m *SetTicketIdRequest) XXX_DiscardUnknown() {
	xxx_messageInfo_SetTicketIdRequest.DiscardUnknown(m)
}

var xxx_messageInfo_SetTicketIdRequest proto.InternalMessageInfo

func (m *SetTicketIdRequest) GetId() string {
	if m != nil {
		return m.Id
	}
	return ""
}

func (m *SetTicketIdRequest) GetTicketId() string {
	if m != nil {
		return m.TicketId
	}
	return ""
}

func init() {
	proto.RegisterEnum("inside_api.Status", Status_name, Status_value)
	proto.RegisterType((*ReceiptRequest)(nil), "inside_api.ReceiptRequest")
	proto.RegisterType((*SetRequestStatusRequest)(nil), "inside_api.SetRequestStatusRequest")
	proto.RegisterType((*QueryByStatus)(nil), "inside_api.QueryByStatus")
	proto.RegisterType((*SetTicketIdRequest)(nil), "inside_api.SetTicketIdRequest")
}

func init() { proto.RegisterFile("requests.proto", fileDescriptor_9c9ccec99da7c9b4) }

var fileDescriptor_9c9ccec99da7c9b4 = []byte{
	// 258 bytes of a gzipped FileDescriptorProto
	0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0xff, 0x84, 0x91, 0x31, 0x4f, 0xc3, 0x30,
	0x10, 0x46, 0x49, 0x4a, 0xa3, 0xf6, 0xa0, 0xc1, 0xf2, 0x00, 0x11, 0x13, 0xca, 0x84, 0x3a, 0x64,
	0x80, 0x91, 0x05, 0x31, 0x54, 0x74, 0x83, 0x04, 0x66, 0x64, 0x72, 0x87, 0xb0, 0x8a, 0xec, 0xc4,
	0x3e, 0x0f, 0xfd, 0xf7, 0xa8, 0x89, 0x29, 0x2c, 0x15, 0xe3, 0x9d, 0xdf, 0xfb, 0x7c, 0xf6, 0x41,
	0xee, 0xa8, 0x0f, 0xe4, 0xd9, 0x57, 0x9d, 0xb3, 0x6c, 0x25, 0x68, 0xe3, 0x35, 0xd2, 0x9b, 0xea,
	0x74, 0xf9, 0x08, 0x79, 0x4d, 0x2d, 0xe9, 0x8e, 0xeb, 0x11, 0x92, 0x39, 0xa4, 0x1a, 0x8b, 0xe4,
	0x2a, 0xb9, 0x9e, 0xd7, 0xa9, 0x46, 0x79, 0x0e, 0x59, 0xf0, 0xe4, 0xd6, 0x58, 0xa4, 0x43, 0x2f,
	0x56, 0x3b, 0xae, 0x77, 0xc5, 0x64, 0xe4, 0x7a, 0x57, 0xbe, 0xc2, 0x45, 0x43, 0x3f, 0x29, 0x0d,
	0x2b, 0x0e, 0xfe, 0x50, 0xe4, 0x12, 0x32, 0x3f, 0x00, 0x43, 0x64, 0x7e, 0x23, 0xab, 0xdf, 0x89,
	0xaa, 0xa8, 0x46, 0xa2, 0xbc, 0x83, 0xc5, 0x73, 0x20, 0xb7, 0x7d, 0xd8, 0x8e, 0x07, 0x7f, 0xe4,
	0xe4, 0x5f, 0xf9, 0x1e, 0x64, 0x43, 0xfc, 0xa2, 0xdb, 0x0d, 0xf1, 0x1a, 0x0f, 0x8d, 0x73, 0x09,
	0x33, 0x8e, 0x48, 0x7c, 0xe3, 0xbe, 0x5e, 0x2a, 0xc8, 0xe2, 0xbd, 0x0b, 0x98, 0x07, 0x83, 0xf4,
	0xa1, 0x0d, 0xa1, 0x38, 0x92, 0x67, 0x70, 0xd2, 0x7e, 0x52, 0xbb, 0x79, 0x52, 0xde, 0x13, 0x8a,
	0x64, 0xdf, 0x58, 0x29, 0xfd, 0x45, 0x28, 0xd2, 0x9d, 0x10, 0x3f, 0x9e, 0x50, 0x4c, 0xe4, 0x1c,
	0xa6, 0xe4, 0x9c, 0x75, 0xe2, 0x58, 0x9e, 0xc2, 0xcc, 0x58, 0x5e, 0xd9, 0x60, 0x50, 0x4c, 0xdf,
	0xb3, 0x61, 0x2b, 0xb7, 0xdf, 0x01, 0x00, 0x00, 0xff, 0xff, 0x5a, 0xf2, 0x0d, 0xf2, 0xa7, 0x01,
	0x00, 0x00,
}
