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

type GetLoginLinkRequest struct {
	TelegramId           int32    `protobuf:"varint,1,opt,name=telegramId,proto3" json:"telegramId,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *GetLoginLinkRequest) Reset()         { *m = GetLoginLinkRequest{} }
func (m *GetLoginLinkRequest) String() string { return proto.CompactTextString(m) }
func (*GetLoginLinkRequest) ProtoMessage()    {}
func (*GetLoginLinkRequest) Descriptor() ([]byte, []int) {
	return fileDescriptor_b6d125f880f9ca35, []int{0}
}

func (m *GetLoginLinkRequest) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_GetLoginLinkRequest.Unmarshal(m, b)
}
func (m *GetLoginLinkRequest) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_GetLoginLinkRequest.Marshal(b, m, deterministic)
}
func (m *GetLoginLinkRequest) XXX_Merge(src proto.Message) {
	xxx_messageInfo_GetLoginLinkRequest.Merge(m, src)
}
func (m *GetLoginLinkRequest) XXX_Size() int {
	return xxx_messageInfo_GetLoginLinkRequest.Size(m)
}
func (m *GetLoginLinkRequest) XXX_DiscardUnknown() {
	xxx_messageInfo_GetLoginLinkRequest.DiscardUnknown(m)
}

var xxx_messageInfo_GetLoginLinkRequest proto.InternalMessageInfo

func (m *GetLoginLinkRequest) GetTelegramId() int32 {
	if m != nil {
		return m.TelegramId
	}
	return 0
}

type GetUserRequest struct {
	TelegramId           int32    `protobuf:"varint,1,opt,name=telegramId,proto3" json:"telegramId,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *GetUserRequest) Reset()         { *m = GetUserRequest{} }
func (m *GetUserRequest) String() string { return proto.CompactTextString(m) }
func (*GetUserRequest) ProtoMessage()    {}
func (*GetUserRequest) Descriptor() ([]byte, []int) {
	return fileDescriptor_b6d125f880f9ca35, []int{1}
}

func (m *GetUserRequest) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_GetUserRequest.Unmarshal(m, b)
}
func (m *GetUserRequest) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_GetUserRequest.Marshal(b, m, deterministic)
}
func (m *GetUserRequest) XXX_Merge(src proto.Message) {
	xxx_messageInfo_GetUserRequest.Merge(m, src)
}
func (m *GetUserRequest) XXX_Size() int {
	return xxx_messageInfo_GetUserRequest.Size(m)
}
func (m *GetUserRequest) XXX_DiscardUnknown() {
	xxx_messageInfo_GetUserRequest.DiscardUnknown(m)
}

var xxx_messageInfo_GetUserRequest proto.InternalMessageInfo

func (m *GetUserRequest) GetTelegramId() int32 {
	if m != nil {
		return m.TelegramId
	}
	return 0
}

type LoginLinkResponse struct {
	Url                  string   `protobuf:"bytes,1,opt,name=url,proto3" json:"url,omitempty"`
	Expiration           int64    `protobuf:"varint,2,opt,name=expiration,proto3" json:"expiration,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *LoginLinkResponse) Reset()         { *m = LoginLinkResponse{} }
func (m *LoginLinkResponse) String() string { return proto.CompactTextString(m) }
func (*LoginLinkResponse) ProtoMessage()    {}
func (*LoginLinkResponse) Descriptor() ([]byte, []int) {
	return fileDescriptor_b6d125f880f9ca35, []int{2}
}

func (m *LoginLinkResponse) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_LoginLinkResponse.Unmarshal(m, b)
}
func (m *LoginLinkResponse) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_LoginLinkResponse.Marshal(b, m, deterministic)
}
func (m *LoginLinkResponse) XXX_Merge(src proto.Message) {
	xxx_messageInfo_LoginLinkResponse.Merge(m, src)
}
func (m *LoginLinkResponse) XXX_Size() int {
	return xxx_messageInfo_LoginLinkResponse.Size(m)
}
func (m *LoginLinkResponse) XXX_DiscardUnknown() {
	xxx_messageInfo_LoginLinkResponse.DiscardUnknown(m)
}

var xxx_messageInfo_LoginLinkResponse proto.InternalMessageInfo

func (m *LoginLinkResponse) GetUrl() string {
	if m != nil {
		return m.Url
	}
	return ""
}

func (m *LoginLinkResponse) GetExpiration() int64 {
	if m != nil {
		return m.Expiration
	}
	return 0
}

type AddReceiptRequest struct {
	UserId               string   `protobuf:"bytes,1,opt,name=userId,proto3" json:"userId,omitempty"`
	ReceiptQr            string   `protobuf:"bytes,2,opt,name=receiptQr,proto3" json:"receiptQr,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *AddReceiptRequest) Reset()         { *m = AddReceiptRequest{} }
func (m *AddReceiptRequest) String() string { return proto.CompactTextString(m) }
func (*AddReceiptRequest) ProtoMessage()    {}
func (*AddReceiptRequest) Descriptor() ([]byte, []int) {
	return fileDescriptor_b6d125f880f9ca35, []int{3}
}

func (m *AddReceiptRequest) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_AddReceiptRequest.Unmarshal(m, b)
}
func (m *AddReceiptRequest) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_AddReceiptRequest.Marshal(b, m, deterministic)
}
func (m *AddReceiptRequest) XXX_Merge(src proto.Message) {
	xxx_messageInfo_AddReceiptRequest.Merge(m, src)
}
func (m *AddReceiptRequest) XXX_Size() int {
	return xxx_messageInfo_AddReceiptRequest.Size(m)
}
func (m *AddReceiptRequest) XXX_DiscardUnknown() {
	xxx_messageInfo_AddReceiptRequest.DiscardUnknown(m)
}

var xxx_messageInfo_AddReceiptRequest proto.InternalMessageInfo

func (m *AddReceiptRequest) GetUserId() string {
	if m != nil {
		return m.UserId
	}
	return ""
}

func (m *AddReceiptRequest) GetReceiptQr() string {
	if m != nil {
		return m.ReceiptQr
	}
	return ""
}

type AddReceiptResponse struct {
	Error                string   `protobuf:"bytes,1,opt,name=error,proto3" json:"error,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *AddReceiptResponse) Reset()         { *m = AddReceiptResponse{} }
func (m *AddReceiptResponse) String() string { return proto.CompactTextString(m) }
func (*AddReceiptResponse) ProtoMessage()    {}
func (*AddReceiptResponse) Descriptor() ([]byte, []int) {
	return fileDescriptor_b6d125f880f9ca35, []int{4}
}

func (m *AddReceiptResponse) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_AddReceiptResponse.Unmarshal(m, b)
}
func (m *AddReceiptResponse) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_AddReceiptResponse.Marshal(b, m, deterministic)
}
func (m *AddReceiptResponse) XXX_Merge(src proto.Message) {
	xxx_messageInfo_AddReceiptResponse.Merge(m, src)
}
func (m *AddReceiptResponse) XXX_Size() int {
	return xxx_messageInfo_AddReceiptResponse.Size(m)
}
func (m *AddReceiptResponse) XXX_DiscardUnknown() {
	xxx_messageInfo_AddReceiptResponse.DiscardUnknown(m)
}

var xxx_messageInfo_AddReceiptResponse proto.InternalMessageInfo

func (m *AddReceiptResponse) GetError() string {
	if m != nil {
		return m.Error
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
	return fileDescriptor_b6d125f880f9ca35, []int{5}
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

type User struct {
	UserId               string   `protobuf:"bytes,1,opt,name=userId,proto3" json:"userId,omitempty"`
	TelegramId           int32    `protobuf:"varint,2,opt,name=telegramId,proto3" json:"telegramId,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *User) Reset()         { *m = User{} }
func (m *User) String() string { return proto.CompactTextString(m) }
func (*User) ProtoMessage()    {}
func (*User) Descriptor() ([]byte, []int) {
	return fileDescriptor_b6d125f880f9ca35, []int{6}
}

func (m *User) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_User.Unmarshal(m, b)
}
func (m *User) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_User.Marshal(b, m, deterministic)
}
func (m *User) XXX_Merge(src proto.Message) {
	xxx_messageInfo_User.Merge(m, src)
}
func (m *User) XXX_Size() int {
	return xxx_messageInfo_User.Size(m)
}
func (m *User) XXX_DiscardUnknown() {
	xxx_messageInfo_User.DiscardUnknown(m)
}

var xxx_messageInfo_User proto.InternalMessageInfo

func (m *User) GetUserId() string {
	if m != nil {
		return m.UserId
	}
	return ""
}

func (m *User) GetTelegramId() int32 {
	if m != nil {
		return m.TelegramId
	}
	return 0
}

type GetUserResponse struct {
	User                 *User    `protobuf:"bytes,1,opt,name=user,proto3" json:"user,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *GetUserResponse) Reset()         { *m = GetUserResponse{} }
func (m *GetUserResponse) String() string { return proto.CompactTextString(m) }
func (*GetUserResponse) ProtoMessage()    {}
func (*GetUserResponse) Descriptor() ([]byte, []int) {
	return fileDescriptor_b6d125f880f9ca35, []int{7}
}

func (m *GetUserResponse) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_GetUserResponse.Unmarshal(m, b)
}
func (m *GetUserResponse) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_GetUserResponse.Marshal(b, m, deterministic)
}
func (m *GetUserResponse) XXX_Merge(src proto.Message) {
	xxx_messageInfo_GetUserResponse.Merge(m, src)
}
func (m *GetUserResponse) XXX_Size() int {
	return xxx_messageInfo_GetUserResponse.Size(m)
}
func (m *GetUserResponse) XXX_DiscardUnknown() {
	xxx_messageInfo_GetUserResponse.DiscardUnknown(m)
}

var xxx_messageInfo_GetUserResponse proto.InternalMessageInfo

func (m *GetUserResponse) GetUser() *User {
	if m != nil {
		return m.User
	}
	return nil
}

type GetUsersResponse struct {
	Users                []*User  `protobuf:"bytes,1,rep,name=users,proto3" json:"users,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *GetUsersResponse) Reset()         { *m = GetUsersResponse{} }
func (m *GetUsersResponse) String() string { return proto.CompactTextString(m) }
func (*GetUsersResponse) ProtoMessage()    {}
func (*GetUsersResponse) Descriptor() ([]byte, []int) {
	return fileDescriptor_b6d125f880f9ca35, []int{8}
}

func (m *GetUsersResponse) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_GetUsersResponse.Unmarshal(m, b)
}
func (m *GetUsersResponse) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_GetUsersResponse.Marshal(b, m, deterministic)
}
func (m *GetUsersResponse) XXX_Merge(src proto.Message) {
	xxx_messageInfo_GetUsersResponse.Merge(m, src)
}
func (m *GetUsersResponse) XXX_Size() int {
	return xxx_messageInfo_GetUsersResponse.Size(m)
}
func (m *GetUsersResponse) XXX_DiscardUnknown() {
	xxx_messageInfo_GetUsersResponse.DiscardUnknown(m)
}

var xxx_messageInfo_GetUsersResponse proto.InternalMessageInfo

func (m *GetUsersResponse) GetUsers() []*User {
	if m != nil {
		return m.Users
	}
	return nil
}

type UserRegistrationRequest struct {
	TelegramId           int32    `protobuf:"varint,1,opt,name=telegramId,proto3" json:"telegramId,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *UserRegistrationRequest) Reset()         { *m = UserRegistrationRequest{} }
func (m *UserRegistrationRequest) String() string { return proto.CompactTextString(m) }
func (*UserRegistrationRequest) ProtoMessage()    {}
func (*UserRegistrationRequest) Descriptor() ([]byte, []int) {
	return fileDescriptor_b6d125f880f9ca35, []int{9}
}

func (m *UserRegistrationRequest) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_UserRegistrationRequest.Unmarshal(m, b)
}
func (m *UserRegistrationRequest) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_UserRegistrationRequest.Marshal(b, m, deterministic)
}
func (m *UserRegistrationRequest) XXX_Merge(src proto.Message) {
	xxx_messageInfo_UserRegistrationRequest.Merge(m, src)
}
func (m *UserRegistrationRequest) XXX_Size() int {
	return xxx_messageInfo_UserRegistrationRequest.Size(m)
}
func (m *UserRegistrationRequest) XXX_DiscardUnknown() {
	xxx_messageInfo_UserRegistrationRequest.DiscardUnknown(m)
}

var xxx_messageInfo_UserRegistrationRequest proto.InternalMessageInfo

func (m *UserRegistrationRequest) GetTelegramId() int32 {
	if m != nil {
		return m.TelegramId
	}
	return 0
}

type UserRegistrationResponse struct {
	UserId               string   `protobuf:"bytes,1,opt,name=userId,proto3" json:"userId,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *UserRegistrationResponse) Reset()         { *m = UserRegistrationResponse{} }
func (m *UserRegistrationResponse) String() string { return proto.CompactTextString(m) }
func (*UserRegistrationResponse) ProtoMessage()    {}
func (*UserRegistrationResponse) Descriptor() ([]byte, []int) {
	return fileDescriptor_b6d125f880f9ca35, []int{10}
}

func (m *UserRegistrationResponse) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_UserRegistrationResponse.Unmarshal(m, b)
}
func (m *UserRegistrationResponse) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_UserRegistrationResponse.Marshal(b, m, deterministic)
}
func (m *UserRegistrationResponse) XXX_Merge(src proto.Message) {
	xxx_messageInfo_UserRegistrationResponse.Merge(m, src)
}
func (m *UserRegistrationResponse) XXX_Size() int {
	return xxx_messageInfo_UserRegistrationResponse.Size(m)
}
func (m *UserRegistrationResponse) XXX_DiscardUnknown() {
	xxx_messageInfo_UserRegistrationResponse.DiscardUnknown(m)
}

var xxx_messageInfo_UserRegistrationResponse proto.InternalMessageInfo

func (m *UserRegistrationResponse) GetUserId() string {
	if m != nil {
		return m.UserId
	}
	return ""
}

func init() {
	proto.RegisterType((*GetLoginLinkRequest)(nil), "inside_api.GetLoginLinkRequest")
	proto.RegisterType((*GetUserRequest)(nil), "inside_api.GetUserRequest")
	proto.RegisterType((*LoginLinkResponse)(nil), "inside_api.LoginLinkResponse")
	proto.RegisterType((*AddReceiptRequest)(nil), "inside_api.AddReceiptRequest")
	proto.RegisterType((*AddReceiptResponse)(nil), "inside_api.AddReceiptResponse")
	proto.RegisterType((*NoParams)(nil), "inside_api.NoParams")
	proto.RegisterType((*User)(nil), "inside_api.User")
	proto.RegisterType((*GetUserResponse)(nil), "inside_api.GetUserResponse")
	proto.RegisterType((*GetUsersResponse)(nil), "inside_api.GetUsersResponse")
	proto.RegisterType((*UserRegistrationRequest)(nil), "inside_api.UserRegistrationRequest")
	proto.RegisterType((*UserRegistrationResponse)(nil), "inside_api.UserRegistrationResponse")
}

func init() { proto.RegisterFile("contracts.proto", fileDescriptor_b6d125f880f9ca35) }

var fileDescriptor_b6d125f880f9ca35 = []byte{
	// 420 bytes of a gzipped FileDescriptorProto
	0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0xff, 0x8c, 0x53, 0xd1, 0xae, 0xd2, 0x40,
	0x10, 0xbd, 0xbd, 0xbd, 0x5c, 0xe9, 0x40, 0x04, 0x56, 0xa2, 0x4d, 0x45, 0x24, 0x2b, 0x31, 0xc4,
	0x07, 0x62, 0x30, 0xc6, 0xe8, 0x83, 0x91, 0x44, 0x43, 0x48, 0xd0, 0x60, 0x13, 0x9f, 0x7c, 0x30,
	0x95, 0x4e, 0xc8, 0x46, 0xd8, 0xad, 0xbb, 0x4b, 0xe2, 0x7f, 0xf8, 0xc3, 0xa6, 0xed, 0xb6, 0x2c,
	0xb6, 0xdc, 0xf0, 0xc6, 0xce, 0x9c, 0x39, 0x67, 0x38, 0x67, 0x0a, 0x9d, 0x8d, 0xe0, 0x5a, 0x46,
	0x1b, 0xad, 0xa6, 0x89, 0x14, 0x5a, 0x10, 0x60, 0x5c, 0xb1, 0x18, 0x7f, 0x44, 0x09, 0xa3, 0xaf,
	0xe1, 0xc1, 0x02, 0xf5, 0x4a, 0x6c, 0x19, 0x5f, 0x31, 0xfe, 0x2b, 0xc4, 0xdf, 0x07, 0x54, 0x9a,
	0x0c, 0x01, 0x34, 0xee, 0x70, 0x2b, 0xa3, 0xfd, 0x32, 0xf6, 0x9d, 0x91, 0x33, 0x69, 0x84, 0x56,
	0x85, 0xbe, 0x84, 0xfb, 0x0b, 0xd4, 0xdf, 0x14, 0xca, 0x4b, 0x27, 0x3e, 0x41, 0xcf, 0x52, 0x51,
	0x89, 0xe0, 0x0a, 0x49, 0x17, 0xdc, 0x83, 0xdc, 0x65, 0x68, 0x2f, 0x4c, 0x7f, 0xa6, 0x34, 0xf8,
	0x27, 0x61, 0x32, 0xd2, 0x4c, 0x70, 0xff, 0x7a, 0xe4, 0x4c, 0xdc, 0xd0, 0xaa, 0xd0, 0x25, 0xf4,
	0xe6, 0x71, 0x1c, 0xe2, 0x06, 0x59, 0xa2, 0x0b, 0xed, 0x87, 0x70, 0x7b, 0x50, 0x28, 0x8d, 0xae,
	0x17, 0x9a, 0x17, 0x19, 0x80, 0x27, 0x73, 0xe4, 0x57, 0x99, 0x71, 0x79, 0xe1, 0xb1, 0x40, 0x5f,
	0x00, 0xb1, 0xa9, 0xcc, 0x4a, 0x7d, 0x68, 0xa0, 0x94, 0x42, 0x1a, 0xaa, 0xfc, 0x41, 0x01, 0x9a,
	0x5f, 0xc4, 0x3a, 0x92, 0xd1, 0x5e, 0xd1, 0xf7, 0x70, 0x93, 0xfe, 0xf1, 0xb3, 0xaa, 0xa7, 0x4e,
	0x5c, 0x57, 0x9c, 0x78, 0x03, 0x9d, 0xd2, 0x3b, 0x23, 0x3a, 0x86, 0x9b, 0x74, 0x38, 0x23, 0x6a,
	0xcd, 0xba, 0xd3, 0x63, 0x40, 0xd3, 0x0c, 0x97, 0x75, 0xe9, 0x3b, 0xe8, 0x9a, 0x41, 0x55, 0x4e,
	0x3e, 0x87, 0x46, 0xda, 0x53, 0xbe, 0x33, 0x72, 0x6b, 0x47, 0xf3, 0x36, 0x7d, 0x0b, 0x8f, 0x72,
	0xc5, 0x2d, 0x53, 0x3a, 0xf7, 0xf2, 0xd2, 0xe4, 0x66, 0xe0, 0x57, 0x47, 0x8d, 0xfc, 0x19, 0x0f,
	0x66, 0x7f, 0x5d, 0x68, 0x2d, 0xb9, 0x46, 0xc9, 0xa3, 0xdd, 0x3c, 0x61, 0x64, 0x0d, 0x6d, 0xfb,
	0xcc, 0xc8, 0x53, 0x7b, 0xcf, 0x9a, 0x03, 0x0c, 0x9e, 0xd8, 0x80, 0xca, 0xe1, 0xd0, 0x2b, 0xf2,
	0x19, 0xe0, 0x98, 0x1e, 0x39, 0x81, 0x57, 0x0e, 0x24, 0x18, 0x9e, 0x6b, 0x97, 0x74, 0x1f, 0xa0,
	0x59, 0x78, 0x4b, 0xfa, 0x36, 0xba, 0x88, 0x3d, 0x18, 0xfc, 0xb7, 0xf2, 0x49, 0x0e, 0xf4, 0x8a,
	0x7c, 0x84, 0x7b, 0xa6, 0x4a, 0x82, 0x1a, 0x68, 0xb1, 0xca, 0xe3, 0xda, 0x5e, 0xc9, 0xf2, 0x1d,
	0xda, 0xb9, 0xd1, 0x28, 0x33, 0xaa, 0x67, 0x95, 0x40, 0xab, 0x09, 0x06, 0xe3, 0xbb, 0x41, 0x05,
	0xf9, 0xcf, 0xdb, 0xec, 0xfb, 0x7f, 0xf5, 0x2f, 0x00, 0x00, 0xff, 0xff, 0xb4, 0x35, 0x12, 0xbf,
	0x12, 0x04, 0x00, 0x00,
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

// InternalApiServer is the server API for InternalApi service.
type InternalApiServer interface {
	GetLoginLink(context.Context, *GetLoginLinkRequest) (*LoginLinkResponse, error)
	AddReceipt(context.Context, *AddReceiptRequest) (*AddReceiptResponse, error)
	GetUsers(context.Context, *NoParams) (*GetUsersResponse, error)
	GetUser(context.Context, *GetUserRequest) (*GetUserResponse, error)
	RegisterUser(context.Context, *UserRegistrationRequest) (*UserRegistrationResponse, error)
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
	},
	Streams:  []grpc.StreamDesc{},
	Metadata: "contracts.proto",
}
