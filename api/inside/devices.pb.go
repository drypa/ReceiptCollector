// Code generated by protoc-gen-go. DO NOT EDIT.
// source: devices.proto

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

type GetDevicesRequest struct {
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *GetDevicesRequest) Reset()         { *m = GetDevicesRequest{} }
func (m *GetDevicesRequest) String() string { return proto.CompactTextString(m) }
func (*GetDevicesRequest) ProtoMessage()    {}
func (*GetDevicesRequest) Descriptor() ([]byte, []int) {
	return fileDescriptor_6d27ec3f2c0e2043, []int{0}
}

func (m *GetDevicesRequest) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_GetDevicesRequest.Unmarshal(m, b)
}
func (m *GetDevicesRequest) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_GetDevicesRequest.Marshal(b, m, deterministic)
}
func (m *GetDevicesRequest) XXX_Merge(src proto.Message) {
	xxx_messageInfo_GetDevicesRequest.Merge(m, src)
}
func (m *GetDevicesRequest) XXX_Size() int {
	return xxx_messageInfo_GetDevicesRequest.Size(m)
}
func (m *GetDevicesRequest) XXX_DiscardUnknown() {
	xxx_messageInfo_GetDevicesRequest.DiscardUnknown(m)
}

var xxx_messageInfo_GetDevicesRequest proto.InternalMessageInfo

type Device struct {
	ClientSecret         string   `protobuf:"bytes,1,opt,name=ClientSecret,proto3" json:"ClientSecret,omitempty"`
	SessionId            string   `protobuf:"bytes,2,opt,name=SessionId,proto3" json:"SessionId,omitempty"`
	RefreshToken         string   `protobuf:"bytes,3,opt,name=RefreshToken,proto3" json:"RefreshToken,omitempty"`
	Id                   string   `protobuf:"bytes,4,opt,name=Id,proto3" json:"Id,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *Device) Reset()         { *m = Device{} }
func (m *Device) String() string { return proto.CompactTextString(m) }
func (*Device) ProtoMessage()    {}
func (*Device) Descriptor() ([]byte, []int) {
	return fileDescriptor_6d27ec3f2c0e2043, []int{1}
}

func (m *Device) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_Device.Unmarshal(m, b)
}
func (m *Device) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_Device.Marshal(b, m, deterministic)
}
func (m *Device) XXX_Merge(src proto.Message) {
	xxx_messageInfo_Device.Merge(m, src)
}
func (m *Device) XXX_Size() int {
	return xxx_messageInfo_Device.Size(m)
}
func (m *Device) XXX_DiscardUnknown() {
	xxx_messageInfo_Device.DiscardUnknown(m)
}

var xxx_messageInfo_Device proto.InternalMessageInfo

func (m *Device) GetClientSecret() string {
	if m != nil {
		return m.ClientSecret
	}
	return ""
}

func (m *Device) GetSessionId() string {
	if m != nil {
		return m.SessionId
	}
	return ""
}

func (m *Device) GetRefreshToken() string {
	if m != nil {
		return m.RefreshToken
	}
	return ""
}

func (m *Device) GetId() string {
	if m != nil {
		return m.Id
	}
	return ""
}

type UpdateDeviceRequest struct {
	Device               *Device  `protobuf:"bytes,1,opt,name=device,proto3" json:"device,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *UpdateDeviceRequest) Reset()         { *m = UpdateDeviceRequest{} }
func (m *UpdateDeviceRequest) String() string { return proto.CompactTextString(m) }
func (*UpdateDeviceRequest) ProtoMessage()    {}
func (*UpdateDeviceRequest) Descriptor() ([]byte, []int) {
	return fileDescriptor_6d27ec3f2c0e2043, []int{2}
}

func (m *UpdateDeviceRequest) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_UpdateDeviceRequest.Unmarshal(m, b)
}
func (m *UpdateDeviceRequest) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_UpdateDeviceRequest.Marshal(b, m, deterministic)
}
func (m *UpdateDeviceRequest) XXX_Merge(src proto.Message) {
	xxx_messageInfo_UpdateDeviceRequest.Merge(m, src)
}
func (m *UpdateDeviceRequest) XXX_Size() int {
	return xxx_messageInfo_UpdateDeviceRequest.Size(m)
}
func (m *UpdateDeviceRequest) XXX_DiscardUnknown() {
	xxx_messageInfo_UpdateDeviceRequest.DiscardUnknown(m)
}

var xxx_messageInfo_UpdateDeviceRequest proto.InternalMessageInfo

func (m *UpdateDeviceRequest) GetDevice() *Device {
	if m != nil {
		return m.Device
	}
	return nil
}

func init() {
	proto.RegisterType((*GetDevicesRequest)(nil), "inside_api.GetDevicesRequest")
	proto.RegisterType((*Device)(nil), "inside_api.Device")
	proto.RegisterType((*UpdateDeviceRequest)(nil), "inside_api.UpdateDeviceRequest")
}

func init() { proto.RegisterFile("devices.proto", fileDescriptor_6d27ec3f2c0e2043) }

var fileDescriptor_6d27ec3f2c0e2043 = []byte{
	// 189 bytes of a gzipped FileDescriptorProto
	0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0xff, 0x54, 0x8f, 0x3f, 0xcb, 0xc2, 0x30,
	0x1c, 0x84, 0x69, 0xdf, 0x97, 0x42, 0x7f, 0xef, 0x1f, 0x30, 0x5d, 0x32, 0x38, 0x48, 0x26, 0x71,
	0xe8, 0xa0, 0x9f, 0x40, 0x14, 0xa4, 0x6b, 0xab, 0xb3, 0xd4, 0xe6, 0xc4, 0xa0, 0x24, 0xb5, 0x89,
	0x8e, 0x7e, 0x76, 0x31, 0x29, 0x54, 0xd7, 0xe7, 0x8e, 0xe3, 0x1e, 0xfa, 0x93, 0xb8, 0xab, 0x06,
	0x36, 0x6f, 0x3b, 0xe3, 0x0c, 0x23, 0xa5, 0xad, 0x92, 0xd8, 0xd7, 0xad, 0x12, 0x19, 0x8d, 0x36,
	0x70, 0xeb, 0x90, 0x97, 0xb8, 0xde, 0x60, 0x9d, 0x78, 0x50, 0x12, 0x08, 0x13, 0xf4, 0xbb, 0xba,
	0x28, 0x68, 0x57, 0xa1, 0xe9, 0xe0, 0x78, 0x34, 0x89, 0xa6, 0x69, 0xf9, 0xc1, 0xd8, 0x98, 0xd2,
	0x0a, 0xd6, 0x2a, 0xa3, 0x0b, 0xc9, 0x63, 0x5f, 0x18, 0xc0, 0x6b, 0xa1, 0xc4, 0xb1, 0x83, 0x3d,
	0x6d, 0xcd, 0x19, 0x9a, 0x7f, 0x85, 0x85, 0x77, 0xc6, 0xfe, 0x29, 0x2e, 0x24, 0xff, 0xf6, 0x49,
	0x5c, 0x48, 0xb1, 0xa4, 0x6c, 0xd7, 0xca, 0xda, 0x21, 0xbc, 0xe8, 0x6f, 0xb1, 0x19, 0x25, 0x41,
	0xc4, 0xdf, 0xf8, 0x99, 0xb3, 0x7c, 0x10, 0xc9, 0xfb, 0x6a, 0xdf, 0x38, 0x24, 0x5e, 0x75, 0xf1,
	0x0c, 0x00, 0x00, 0xff, 0xff, 0x37, 0x81, 0x60, 0x27, 0xfb, 0x00, 0x00, 0x00,
}
