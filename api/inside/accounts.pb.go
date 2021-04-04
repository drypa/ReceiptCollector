// Code generated by protoc-gen-go. DO NOT EDIT.
// source: accounts.proto

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
	return fileDescriptor_e1e7723af4c007b7, []int{0}
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
	return fileDescriptor_e1e7723af4c007b7, []int{1}
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
	return fileDescriptor_e1e7723af4c007b7, []int{2}
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
	return fileDescriptor_e1e7723af4c007b7, []int{3}
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
	return fileDescriptor_e1e7723af4c007b7, []int{4}
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
	return fileDescriptor_e1e7723af4c007b7, []int{5}
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
	return fileDescriptor_e1e7723af4c007b7, []int{6}
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
	return fileDescriptor_e1e7723af4c007b7, []int{7}
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
	proto.RegisterType((*User)(nil), "inside_api.User")
	proto.RegisterType((*GetUserResponse)(nil), "inside_api.GetUserResponse")
	proto.RegisterType((*GetUsersResponse)(nil), "inside_api.GetUsersResponse")
	proto.RegisterType((*UserRegistrationRequest)(nil), "inside_api.UserRegistrationRequest")
	proto.RegisterType((*UserRegistrationResponse)(nil), "inside_api.UserRegistrationResponse")
}

func init() { proto.RegisterFile("accounts.proto", fileDescriptor_e1e7723af4c007b7) }

var fileDescriptor_e1e7723af4c007b7 = []byte{
	// 257 bytes of a gzipped FileDescriptorProto
	0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0xff, 0x8c, 0x91, 0xd1, 0x4b, 0xc3, 0x30,
	0x10, 0xc6, 0xe9, 0xba, 0x0d, 0x76, 0xc2, 0xac, 0x15, 0xb4, 0x4f, 0x52, 0x82, 0xc8, 0x9e, 0x8a,
	0x4c, 0x44, 0xf4, 0xc1, 0x37, 0x19, 0xc2, 0x9e, 0x02, 0x3e, 0x4b, 0xdd, 0x8e, 0x12, 0x9c, 0x49,
	0xcd, 0x5d, 0xc0, 0x3f, 0x5f, 0x92, 0x86, 0x59, 0x9c, 0x42, 0xdf, 0x92, 0xbb, 0xfb, 0xdd, 0xf7,
	0xe5, 0x0b, 0xcc, 0xeb, 0xcd, 0xc6, 0x38, 0xcd, 0x54, 0xb5, 0xd6, 0xb0, 0xc9, 0x41, 0x69, 0x52,
	0x5b, 0x7c, 0xad, 0x5b, 0x25, 0x6e, 0xe1, 0x74, 0x85, 0xbc, 0x36, 0x8d, 0xd2, 0x6b, 0xa5, 0xdf,
	0x25, 0x7e, 0x3a, 0x24, 0xce, 0x2f, 0x00, 0x18, 0x77, 0xd8, 0xd8, 0xfa, 0xe3, 0x79, 0x5b, 0x24,
	0x65, 0xb2, 0x98, 0xc8, 0x5e, 0x45, 0x5c, 0xc3, 0x7c, 0x85, 0xfc, 0x42, 0x68, 0x87, 0x12, 0x4f,
	0x70, 0xd2, 0x53, 0xa1, 0xd6, 0x68, 0xc2, 0x3c, 0x83, 0xd4, 0xd9, 0x5d, 0x98, 0x9e, 0x49, 0x7f,
	0xf4, 0x6b, 0xf0, 0xab, 0x55, 0xb6, 0x66, 0x65, 0x74, 0x31, 0x2a, 0x93, 0x45, 0x2a, 0x7b, 0x15,
	0xf1, 0x08, 0x63, 0xaf, 0x9a, 0x9f, 0xc1, 0xd4, 0x11, 0xda, 0x28, 0x35, 0x93, 0xf1, 0xf6, 0xcb,
	0xc6, 0xe8, 0xc0, 0xc6, 0x1d, 0x1c, 0xef, 0x8d, 0x47, 0x13, 0x97, 0x30, 0xf6, 0x70, 0x58, 0x74,
	0xb4, 0xcc, 0xaa, 0x9f, 0x74, 0xaa, 0x30, 0x17, 0xba, 0xe2, 0x01, 0xb2, 0x08, 0xd2, 0x9e, 0xbc,
	0x82, 0x89, 0xef, 0x51, 0x91, 0x94, 0xe9, 0x9f, 0x68, 0xd7, 0x16, 0xf7, 0x70, 0xde, 0x29, 0x36,
	0x8a, 0xb8, 0x7b, 0xc8, 0xd0, 0xd8, 0x96, 0x50, 0x1c, 0xa2, 0x51, 0xfe, 0x9f, 0x0c, 0xde, 0xa6,
	0xe1, 0x9b, 0x6f, 0xbe, 0x03, 0x00, 0x00, 0xff, 0xff, 0xc3, 0x7d, 0x39, 0x62, 0xf8, 0x01, 0x00,
	0x00,
}