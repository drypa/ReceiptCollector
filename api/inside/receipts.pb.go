// Code generated by protoc-gen-go. DO NOT EDIT.
// source: receipts.proto

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
	return fileDescriptor_ef1c006035b8d22c, []int{0}
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
	return fileDescriptor_ef1c006035b8d22c, []int{1}
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

type GetReceiptsRequest struct {
	UserId               string   `protobuf:"bytes,1,opt,name=userId,proto3" json:"userId,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *GetReceiptsRequest) Reset()         { *m = GetReceiptsRequest{} }
func (m *GetReceiptsRequest) String() string { return proto.CompactTextString(m) }
func (*GetReceiptsRequest) ProtoMessage()    {}
func (*GetReceiptsRequest) Descriptor() ([]byte, []int) {
	return fileDescriptor_ef1c006035b8d22c, []int{2}
}

func (m *GetReceiptsRequest) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_GetReceiptsRequest.Unmarshal(m, b)
}
func (m *GetReceiptsRequest) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_GetReceiptsRequest.Marshal(b, m, deterministic)
}
func (m *GetReceiptsRequest) XXX_Merge(src proto.Message) {
	xxx_messageInfo_GetReceiptsRequest.Merge(m, src)
}
func (m *GetReceiptsRequest) XXX_Size() int {
	return xxx_messageInfo_GetReceiptsRequest.Size(m)
}
func (m *GetReceiptsRequest) XXX_DiscardUnknown() {
	xxx_messageInfo_GetReceiptsRequest.DiscardUnknown(m)
}

var xxx_messageInfo_GetReceiptsRequest proto.InternalMessageInfo

func (m *GetReceiptsRequest) GetUserId() string {
	if m != nil {
		return m.UserId
	}
	return ""
}

type AddRawTicketRequest struct {
	Details              *TicketDetails `protobuf:"bytes,1,opt,name=details,proto3" json:"details,omitempty"`
	XXX_NoUnkeyedLiteral struct{}       `json:"-"`
	XXX_unrecognized     []byte         `json:"-"`
	XXX_sizecache        int32          `json:"-"`
}

func (m *AddRawTicketRequest) Reset()         { *m = AddRawTicketRequest{} }
func (m *AddRawTicketRequest) String() string { return proto.CompactTextString(m) }
func (*AddRawTicketRequest) ProtoMessage()    {}
func (*AddRawTicketRequest) Descriptor() ([]byte, []int) {
	return fileDescriptor_ef1c006035b8d22c, []int{3}
}

func (m *AddRawTicketRequest) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_AddRawTicketRequest.Unmarshal(m, b)
}
func (m *AddRawTicketRequest) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_AddRawTicketRequest.Marshal(b, m, deterministic)
}
func (m *AddRawTicketRequest) XXX_Merge(src proto.Message) {
	xxx_messageInfo_AddRawTicketRequest.Merge(m, src)
}
func (m *AddRawTicketRequest) XXX_Size() int {
	return xxx_messageInfo_AddRawTicketRequest.Size(m)
}
func (m *AddRawTicketRequest) XXX_DiscardUnknown() {
	xxx_messageInfo_AddRawTicketRequest.DiscardUnknown(m)
}

var xxx_messageInfo_AddRawTicketRequest proto.InternalMessageInfo

func (m *AddRawTicketRequest) GetDetails() *TicketDetails {
	if m != nil {
		return m.Details
	}
	return nil
}

type Receipt struct {
	DateTime             int64    `protobuf:"varint,1,opt,name=DateTime,proto3" json:"DateTime,omitempty"`
	CashTotalSum         int64    `protobuf:"varint,2,opt,name=CashTotalSum,proto3" json:"CashTotalSum,omitempty"`
	Code                 int32    `protobuf:"varint,3,opt,name=Code,proto3" json:"Code,omitempty"`
	CreditSum            int64    `protobuf:"varint,4,opt,name=CreditSum,proto3" json:"CreditSum,omitempty"`
	EcashTotalSum        int64    `protobuf:"varint,5,opt,name=EcashTotalSum,proto3" json:"EcashTotalSum,omitempty"`
	FiscalDocumentNumber int32    `protobuf:"varint,6,opt,name=FiscalDocumentNumber,proto3" json:"FiscalDocumentNumber,omitempty"`
	FnsUrl               string   `protobuf:"bytes,9,opt,name=FnsUrl,proto3" json:"FnsUrl,omitempty"`
	Items                []*Item  `protobuf:"bytes,10,rep,name=Items,proto3" json:"Items,omitempty"`
	UserInn              string   `protobuf:"bytes,11,opt,name=UserInn,proto3" json:"UserInn,omitempty"`
	Nds10                int64    `protobuf:"varint,12,opt,name=Nds10,proto3" json:"Nds10,omitempty"`
	Nds18                int64    `protobuf:"varint,13,opt,name=Nds18,proto3" json:"Nds18,omitempty"`
	OperationType        int32    `protobuf:"varint,14,opt,name=OperationType,proto3" json:"OperationType,omitempty"`
	Operator             string   `protobuf:"bytes,15,opt,name=Operator,proto3" json:"Operator,omitempty"`
	PrepaidSum           int64    `protobuf:"varint,16,opt,name=PrepaidSum,proto3" json:"PrepaidSum,omitempty"`
	ProvisionSum         int64    `protobuf:"varint,17,opt,name=ProvisionSum,proto3" json:"ProvisionSum,omitempty"`
	RequestNumber        int32    `protobuf:"varint,18,opt,name=RequestNumber,proto3" json:"RequestNumber,omitempty"`
	RetailPlace          string   `protobuf:"bytes,19,opt,name=RetailPlace,proto3" json:"RetailPlace,omitempty"`
	RetailPlaceAddress   string   `protobuf:"bytes,20,opt,name=RetailPlaceAddress,proto3" json:"RetailPlaceAddress,omitempty"`
	TotalSum             int64    `protobuf:"varint,21,opt,name=TotalSum,proto3" json:"TotalSum,omitempty"`
	User                 string   `protobuf:"bytes,22,opt,name=User,proto3" json:"User,omitempty"`
	PostpaymentSum       uint64   `protobuf:"varint,23,opt,name=PostpaymentSum,proto3" json:"PostpaymentSum,omitempty"`
	CounterSubmissionSum uint64   `protobuf:"varint,24,opt,name=CounterSubmissionSum,proto3" json:"CounterSubmissionSum,omitempty"`
	FiscalDriveNumber    string   `protobuf:"bytes,25,opt,name=FiscalDriveNumber,proto3" json:"FiscalDriveNumber,omitempty"`
	FiscalSign           uint32   `protobuf:"varint,26,opt,name=FiscalSign,proto3" json:"FiscalSign,omitempty"`
	KktRegId             string   `protobuf:"bytes,27,opt,name=KktRegId,proto3" json:"KktRegId,omitempty"`
	PrepaymentSum        uint64   `protobuf:"varint,28,opt,name=PrepaymentSum,proto3" json:"PrepaymentSum,omitempty"`
	ProtocolVersion      uint32   `protobuf:"varint,29,opt,name=ProtocolVersion,proto3" json:"ProtocolVersion,omitempty"`
	ReceiptCode          uint32   `protobuf:"varint,30,opt,name=ReceiptCode,proto3" json:"ReceiptCode,omitempty"`
	SenderAddress        string   `protobuf:"bytes,31,opt,name=SenderAddress,proto3" json:"SenderAddress,omitempty"`
	ShiftNumber          uint32   `protobuf:"varint,32,opt,name=ShiftNumber,proto3" json:"ShiftNumber,omitempty"`
	TaxationType         uint32   `protobuf:"varint,33,opt,name=TaxationType,proto3" json:"TaxationType,omitempty"`
	Id                   string   `protobuf:"bytes,34,opt,name=Id,proto3" json:"Id,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *Receipt) Reset()         { *m = Receipt{} }
func (m *Receipt) String() string { return proto.CompactTextString(m) }
func (*Receipt) ProtoMessage()    {}
func (*Receipt) Descriptor() ([]byte, []int) {
	return fileDescriptor_ef1c006035b8d22c, []int{4}
}

func (m *Receipt) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_Receipt.Unmarshal(m, b)
}
func (m *Receipt) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_Receipt.Marshal(b, m, deterministic)
}
func (m *Receipt) XXX_Merge(src proto.Message) {
	xxx_messageInfo_Receipt.Merge(m, src)
}
func (m *Receipt) XXX_Size() int {
	return xxx_messageInfo_Receipt.Size(m)
}
func (m *Receipt) XXX_DiscardUnknown() {
	xxx_messageInfo_Receipt.DiscardUnknown(m)
}

var xxx_messageInfo_Receipt proto.InternalMessageInfo

func (m *Receipt) GetDateTime() int64 {
	if m != nil {
		return m.DateTime
	}
	return 0
}

func (m *Receipt) GetCashTotalSum() int64 {
	if m != nil {
		return m.CashTotalSum
	}
	return 0
}

func (m *Receipt) GetCode() int32 {
	if m != nil {
		return m.Code
	}
	return 0
}

func (m *Receipt) GetCreditSum() int64 {
	if m != nil {
		return m.CreditSum
	}
	return 0
}

func (m *Receipt) GetEcashTotalSum() int64 {
	if m != nil {
		return m.EcashTotalSum
	}
	return 0
}

func (m *Receipt) GetFiscalDocumentNumber() int32 {
	if m != nil {
		return m.FiscalDocumentNumber
	}
	return 0
}

func (m *Receipt) GetFnsUrl() string {
	if m != nil {
		return m.FnsUrl
	}
	return ""
}

func (m *Receipt) GetItems() []*Item {
	if m != nil {
		return m.Items
	}
	return nil
}

func (m *Receipt) GetUserInn() string {
	if m != nil {
		return m.UserInn
	}
	return ""
}

func (m *Receipt) GetNds10() int64 {
	if m != nil {
		return m.Nds10
	}
	return 0
}

func (m *Receipt) GetNds18() int64 {
	if m != nil {
		return m.Nds18
	}
	return 0
}

func (m *Receipt) GetOperationType() int32 {
	if m != nil {
		return m.OperationType
	}
	return 0
}

func (m *Receipt) GetOperator() string {
	if m != nil {
		return m.Operator
	}
	return ""
}

func (m *Receipt) GetPrepaidSum() int64 {
	if m != nil {
		return m.PrepaidSum
	}
	return 0
}

func (m *Receipt) GetProvisionSum() int64 {
	if m != nil {
		return m.ProvisionSum
	}
	return 0
}

func (m *Receipt) GetRequestNumber() int32 {
	if m != nil {
		return m.RequestNumber
	}
	return 0
}

func (m *Receipt) GetRetailPlace() string {
	if m != nil {
		return m.RetailPlace
	}
	return ""
}

func (m *Receipt) GetRetailPlaceAddress() string {
	if m != nil {
		return m.RetailPlaceAddress
	}
	return ""
}

func (m *Receipt) GetTotalSum() int64 {
	if m != nil {
		return m.TotalSum
	}
	return 0
}

func (m *Receipt) GetUser() string {
	if m != nil {
		return m.User
	}
	return ""
}

func (m *Receipt) GetPostpaymentSum() uint64 {
	if m != nil {
		return m.PostpaymentSum
	}
	return 0
}

func (m *Receipt) GetCounterSubmissionSum() uint64 {
	if m != nil {
		return m.CounterSubmissionSum
	}
	return 0
}

func (m *Receipt) GetFiscalDriveNumber() string {
	if m != nil {
		return m.FiscalDriveNumber
	}
	return ""
}

func (m *Receipt) GetFiscalSign() uint32 {
	if m != nil {
		return m.FiscalSign
	}
	return 0
}

func (m *Receipt) GetKktRegId() string {
	if m != nil {
		return m.KktRegId
	}
	return ""
}

func (m *Receipt) GetPrepaymentSum() uint64 {
	if m != nil {
		return m.PrepaymentSum
	}
	return 0
}

func (m *Receipt) GetProtocolVersion() uint32 {
	if m != nil {
		return m.ProtocolVersion
	}
	return 0
}

func (m *Receipt) GetReceiptCode() uint32 {
	if m != nil {
		return m.ReceiptCode
	}
	return 0
}

func (m *Receipt) GetSenderAddress() string {
	if m != nil {
		return m.SenderAddress
	}
	return ""
}

func (m *Receipt) GetShiftNumber() uint32 {
	if m != nil {
		return m.ShiftNumber
	}
	return 0
}

func (m *Receipt) GetTaxationType() uint32 {
	if m != nil {
		return m.TaxationType
	}
	return 0
}

func (m *Receipt) GetId() string {
	if m != nil {
		return m.Id
	}
	return ""
}

type Item struct {
	Name                   string   `protobuf:"bytes,1,opt,name=Name,proto3" json:"Name,omitempty"`
	Nds                    int32    `protobuf:"varint,2,opt,name=Nds,proto3" json:"Nds,omitempty"`
	NdsSum                 int64    `protobuf:"varint,3,opt,name=NdsSum,proto3" json:"NdsSum,omitempty"`
	PaymentType            int32    `protobuf:"varint,4,opt,name=PaymentType,proto3" json:"PaymentType,omitempty"`
	Price                  int64    `protobuf:"varint,5,opt,name=Price,proto3" json:"Price,omitempty"`
	Quantity               float32  `protobuf:"fixed32,6,opt,name=Quantity,proto3" json:"Quantity,omitempty"`
	Sum                    int64    `protobuf:"varint,7,opt,name=Sum,proto3" json:"Sum,omitempty"`
	CalculationSubjectSign uint32   `protobuf:"varint,8,opt,name=CalculationSubjectSign,proto3" json:"CalculationSubjectSign,omitempty"`
	CalculationTypeSign    uint32   `protobuf:"varint,9,opt,name=CalculationTypeSign,proto3" json:"CalculationTypeSign,omitempty"`
	NdsRate                uint32   `protobuf:"varint,10,opt,name=NdsRate,proto3" json:"NdsRate,omitempty"`
	XXX_NoUnkeyedLiteral   struct{} `json:"-"`
	XXX_unrecognized       []byte   `json:"-"`
	XXX_sizecache          int32    `json:"-"`
}

func (m *Item) Reset()         { *m = Item{} }
func (m *Item) String() string { return proto.CompactTextString(m) }
func (*Item) ProtoMessage()    {}
func (*Item) Descriptor() ([]byte, []int) {
	return fileDescriptor_ef1c006035b8d22c, []int{5}
}

func (m *Item) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_Item.Unmarshal(m, b)
}
func (m *Item) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_Item.Marshal(b, m, deterministic)
}
func (m *Item) XXX_Merge(src proto.Message) {
	xxx_messageInfo_Item.Merge(m, src)
}
func (m *Item) XXX_Size() int {
	return xxx_messageInfo_Item.Size(m)
}
func (m *Item) XXX_DiscardUnknown() {
	xxx_messageInfo_Item.DiscardUnknown(m)
}

var xxx_messageInfo_Item proto.InternalMessageInfo

func (m *Item) GetName() string {
	if m != nil {
		return m.Name
	}
	return ""
}

func (m *Item) GetNds() int32 {
	if m != nil {
		return m.Nds
	}
	return 0
}

func (m *Item) GetNdsSum() int64 {
	if m != nil {
		return m.NdsSum
	}
	return 0
}

func (m *Item) GetPaymentType() int32 {
	if m != nil {
		return m.PaymentType
	}
	return 0
}

func (m *Item) GetPrice() int64 {
	if m != nil {
		return m.Price
	}
	return 0
}

func (m *Item) GetQuantity() float32 {
	if m != nil {
		return m.Quantity
	}
	return 0
}

func (m *Item) GetSum() int64 {
	if m != nil {
		return m.Sum
	}
	return 0
}

func (m *Item) GetCalculationSubjectSign() uint32 {
	if m != nil {
		return m.CalculationSubjectSign
	}
	return 0
}

func (m *Item) GetCalculationTypeSign() uint32 {
	if m != nil {
		return m.CalculationTypeSign
	}
	return 0
}

func (m *Item) GetNdsRate() uint32 {
	if m != nil {
		return m.NdsRate
	}
	return 0
}

type Document struct {
	Receipt              *Receipt `protobuf:"bytes,1,opt,name=receipt,proto3" json:"receipt,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *Document) Reset()         { *m = Document{} }
func (m *Document) String() string { return proto.CompactTextString(m) }
func (*Document) ProtoMessage()    {}
func (*Document) Descriptor() ([]byte, []int) {
	return fileDescriptor_ef1c006035b8d22c, []int{6}
}

func (m *Document) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_Document.Unmarshal(m, b)
}
func (m *Document) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_Document.Marshal(b, m, deterministic)
}
func (m *Document) XXX_Merge(src proto.Message) {
	xxx_messageInfo_Document.Merge(m, src)
}
func (m *Document) XXX_Size() int {
	return xxx_messageInfo_Document.Size(m)
}
func (m *Document) XXX_DiscardUnknown() {
	xxx_messageInfo_Document.DiscardUnknown(m)
}

var xxx_messageInfo_Document proto.InternalMessageInfo

func (m *Document) GetReceipt() *Receipt {
	if m != nil {
		return m.Receipt
	}
	return nil
}

type Ticket struct {
	Document             *Document `protobuf:"bytes,1,opt,name=document,proto3" json:"document,omitempty"`
	XXX_NoUnkeyedLiteral struct{}  `json:"-"`
	XXX_unrecognized     []byte    `json:"-"`
	XXX_sizecache        int32     `json:"-"`
}

func (m *Ticket) Reset()         { *m = Ticket{} }
func (m *Ticket) String() string { return proto.CompactTextString(m) }
func (*Ticket) ProtoMessage()    {}
func (*Ticket) Descriptor() ([]byte, []int) {
	return fileDescriptor_ef1c006035b8d22c, []int{7}
}

func (m *Ticket) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_Ticket.Unmarshal(m, b)
}
func (m *Ticket) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_Ticket.Marshal(b, m, deterministic)
}
func (m *Ticket) XXX_Merge(src proto.Message) {
	xxx_messageInfo_Ticket.Merge(m, src)
}
func (m *Ticket) XXX_Size() int {
	return xxx_messageInfo_Ticket.Size(m)
}
func (m *Ticket) XXX_DiscardUnknown() {
	xxx_messageInfo_Ticket.DiscardUnknown(m)
}

var xxx_messageInfo_Ticket proto.InternalMessageInfo

func (m *Ticket) GetDocument() *Document {
	if m != nil {
		return m.Document
	}
	return nil
}

type TicketDetails struct {
	Status               uint32        `protobuf:"varint,1,opt,name=status,proto3" json:"status,omitempty"`
	Id                   string        `protobuf:"bytes,2,opt,name=id,proto3" json:"id,omitempty"`
	Kind                 string        `protobuf:"bytes,3,opt,name=kind,proto3" json:"kind,omitempty"`
	CreatedAt            string        `protobuf:"bytes,4,opt,name=createdAt,proto3" json:"createdAt,omitempty"`
	Qr                   string        `protobuf:"bytes,5,opt,name=qr,proto3" json:"qr,omitempty"`
	Operation            *Operation    `protobuf:"bytes,6,opt,name=operation,proto3" json:"operation,omitempty"`
	Process              []*Process    `protobuf:"bytes,7,rep,name=process,proto3" json:"process,omitempty"`
	Query                *Query        `protobuf:"bytes,8,opt,name=query,proto3" json:"query,omitempty"`
	Ticket               *Ticket       `protobuf:"bytes,9,opt,name=ticket,proto3" json:"ticket,omitempty"`
	Organization         *Organization `protobuf:"bytes,10,opt,name=organization,proto3" json:"organization,omitempty"`
	Seller               *Seller       `protobuf:"bytes,11,opt,name=seller,proto3" json:"seller,omitempty"`
	XXX_NoUnkeyedLiteral struct{}      `json:"-"`
	XXX_unrecognized     []byte        `json:"-"`
	XXX_sizecache        int32         `json:"-"`
}

func (m *TicketDetails) Reset()         { *m = TicketDetails{} }
func (m *TicketDetails) String() string { return proto.CompactTextString(m) }
func (*TicketDetails) ProtoMessage()    {}
func (*TicketDetails) Descriptor() ([]byte, []int) {
	return fileDescriptor_ef1c006035b8d22c, []int{8}
}

func (m *TicketDetails) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_TicketDetails.Unmarshal(m, b)
}
func (m *TicketDetails) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_TicketDetails.Marshal(b, m, deterministic)
}
func (m *TicketDetails) XXX_Merge(src proto.Message) {
	xxx_messageInfo_TicketDetails.Merge(m, src)
}
func (m *TicketDetails) XXX_Size() int {
	return xxx_messageInfo_TicketDetails.Size(m)
}
func (m *TicketDetails) XXX_DiscardUnknown() {
	xxx_messageInfo_TicketDetails.DiscardUnknown(m)
}

var xxx_messageInfo_TicketDetails proto.InternalMessageInfo

func (m *TicketDetails) GetStatus() uint32 {
	if m != nil {
		return m.Status
	}
	return 0
}

func (m *TicketDetails) GetId() string {
	if m != nil {
		return m.Id
	}
	return ""
}

func (m *TicketDetails) GetKind() string {
	if m != nil {
		return m.Kind
	}
	return ""
}

func (m *TicketDetails) GetCreatedAt() string {
	if m != nil {
		return m.CreatedAt
	}
	return ""
}

func (m *TicketDetails) GetQr() string {
	if m != nil {
		return m.Qr
	}
	return ""
}

func (m *TicketDetails) GetOperation() *Operation {
	if m != nil {
		return m.Operation
	}
	return nil
}

func (m *TicketDetails) GetProcess() []*Process {
	if m != nil {
		return m.Process
	}
	return nil
}

func (m *TicketDetails) GetQuery() *Query {
	if m != nil {
		return m.Query
	}
	return nil
}

func (m *TicketDetails) GetTicket() *Ticket {
	if m != nil {
		return m.Ticket
	}
	return nil
}

func (m *TicketDetails) GetOrganization() *Organization {
	if m != nil {
		return m.Organization
	}
	return nil
}

func (m *TicketDetails) GetSeller() *Seller {
	if m != nil {
		return m.Seller
	}
	return nil
}

type Query struct {
	OperationType        uint32   `protobuf:"varint,1,opt,name=operationType,proto3" json:"operationType,omitempty"`
	Sum                  uint64   `protobuf:"varint,2,opt,name=sum,proto3" json:"sum,omitempty"`
	DocumentId           uint32   `protobuf:"varint,3,opt,name=documentId,proto3" json:"documentId,omitempty"`
	FsId                 string   `protobuf:"bytes,4,opt,name=fsId,proto3" json:"fsId,omitempty"`
	FiscalSign           string   `protobuf:"bytes,5,opt,name=fiscalSign,proto3" json:"fiscalSign,omitempty"`
	Date                 string   `protobuf:"bytes,6,opt,name=date,proto3" json:"date,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *Query) Reset()         { *m = Query{} }
func (m *Query) String() string { return proto.CompactTextString(m) }
func (*Query) ProtoMessage()    {}
func (*Query) Descriptor() ([]byte, []int) {
	return fileDescriptor_ef1c006035b8d22c, []int{9}
}

func (m *Query) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_Query.Unmarshal(m, b)
}
func (m *Query) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_Query.Marshal(b, m, deterministic)
}
func (m *Query) XXX_Merge(src proto.Message) {
	xxx_messageInfo_Query.Merge(m, src)
}
func (m *Query) XXX_Size() int {
	return xxx_messageInfo_Query.Size(m)
}
func (m *Query) XXX_DiscardUnknown() {
	xxx_messageInfo_Query.DiscardUnknown(m)
}

var xxx_messageInfo_Query proto.InternalMessageInfo

func (m *Query) GetOperationType() uint32 {
	if m != nil {
		return m.OperationType
	}
	return 0
}

func (m *Query) GetSum() uint64 {
	if m != nil {
		return m.Sum
	}
	return 0
}

func (m *Query) GetDocumentId() uint32 {
	if m != nil {
		return m.DocumentId
	}
	return 0
}

func (m *Query) GetFsId() string {
	if m != nil {
		return m.FsId
	}
	return ""
}

func (m *Query) GetFiscalSign() string {
	if m != nil {
		return m.FiscalSign
	}
	return ""
}

func (m *Query) GetDate() string {
	if m != nil {
		return m.Date
	}
	return ""
}

type Organization struct {
	Name                 string   `protobuf:"bytes,1,opt,name=name,proto3" json:"name,omitempty"`
	Inn                  string   `protobuf:"bytes,2,opt,name=inn,proto3" json:"inn,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *Organization) Reset()         { *m = Organization{} }
func (m *Organization) String() string { return proto.CompactTextString(m) }
func (*Organization) ProtoMessage()    {}
func (*Organization) Descriptor() ([]byte, []int) {
	return fileDescriptor_ef1c006035b8d22c, []int{10}
}

func (m *Organization) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_Organization.Unmarshal(m, b)
}
func (m *Organization) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_Organization.Marshal(b, m, deterministic)
}
func (m *Organization) XXX_Merge(src proto.Message) {
	xxx_messageInfo_Organization.Merge(m, src)
}
func (m *Organization) XXX_Size() int {
	return xxx_messageInfo_Organization.Size(m)
}
func (m *Organization) XXX_DiscardUnknown() {
	xxx_messageInfo_Organization.DiscardUnknown(m)
}

var xxx_messageInfo_Organization proto.InternalMessageInfo

func (m *Organization) GetName() string {
	if m != nil {
		return m.Name
	}
	return ""
}

func (m *Organization) GetInn() string {
	if m != nil {
		return m.Inn
	}
	return ""
}

type Seller struct {
	Name                 string   `protobuf:"bytes,1,opt,name=name,proto3" json:"name,omitempty"`
	Inn                  string   `protobuf:"bytes,2,opt,name=inn,proto3" json:"inn,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *Seller) Reset()         { *m = Seller{} }
func (m *Seller) String() string { return proto.CompactTextString(m) }
func (*Seller) ProtoMessage()    {}
func (*Seller) Descriptor() ([]byte, []int) {
	return fileDescriptor_ef1c006035b8d22c, []int{11}
}

func (m *Seller) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_Seller.Unmarshal(m, b)
}
func (m *Seller) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_Seller.Marshal(b, m, deterministic)
}
func (m *Seller) XXX_Merge(src proto.Message) {
	xxx_messageInfo_Seller.Merge(m, src)
}
func (m *Seller) XXX_Size() int {
	return xxx_messageInfo_Seller.Size(m)
}
func (m *Seller) XXX_DiscardUnknown() {
	xxx_messageInfo_Seller.DiscardUnknown(m)
}

var xxx_messageInfo_Seller proto.InternalMessageInfo

func (m *Seller) GetName() string {
	if m != nil {
		return m.Name
	}
	return ""
}

func (m *Seller) GetInn() string {
	if m != nil {
		return m.Inn
	}
	return ""
}

type Operation struct {
	Date                 string   `protobuf:"bytes,1,opt,name=date,proto3" json:"date,omitempty"`
	Type                 uint32   `protobuf:"varint,2,opt,name=type,proto3" json:"type,omitempty"`
	Sum                  uint64   `protobuf:"varint,3,opt,name=sum,proto3" json:"sum,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *Operation) Reset()         { *m = Operation{} }
func (m *Operation) String() string { return proto.CompactTextString(m) }
func (*Operation) ProtoMessage()    {}
func (*Operation) Descriptor() ([]byte, []int) {
	return fileDescriptor_ef1c006035b8d22c, []int{12}
}

func (m *Operation) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_Operation.Unmarshal(m, b)
}
func (m *Operation) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_Operation.Marshal(b, m, deterministic)
}
func (m *Operation) XXX_Merge(src proto.Message) {
	xxx_messageInfo_Operation.Merge(m, src)
}
func (m *Operation) XXX_Size() int {
	return xxx_messageInfo_Operation.Size(m)
}
func (m *Operation) XXX_DiscardUnknown() {
	xxx_messageInfo_Operation.DiscardUnknown(m)
}

var xxx_messageInfo_Operation proto.InternalMessageInfo

func (m *Operation) GetDate() string {
	if m != nil {
		return m.Date
	}
	return ""
}

func (m *Operation) GetType() uint32 {
	if m != nil {
		return m.Type
	}
	return 0
}

func (m *Operation) GetSum() uint64 {
	if m != nil {
		return m.Sum
	}
	return 0
}

type Process struct {
	Time                 string   `protobuf:"bytes,1,opt,name=time,proto3" json:"time,omitempty"`
	Result               uint32   `protobuf:"varint,2,opt,name=result,proto3" json:"result,omitempty"`
	XXX_NoUnkeyedLiteral struct{} `json:"-"`
	XXX_unrecognized     []byte   `json:"-"`
	XXX_sizecache        int32    `json:"-"`
}

func (m *Process) Reset()         { *m = Process{} }
func (m *Process) String() string { return proto.CompactTextString(m) }
func (*Process) ProtoMessage()    {}
func (*Process) Descriptor() ([]byte, []int) {
	return fileDescriptor_ef1c006035b8d22c, []int{13}
}

func (m *Process) XXX_Unmarshal(b []byte) error {
	return xxx_messageInfo_Process.Unmarshal(m, b)
}
func (m *Process) XXX_Marshal(b []byte, deterministic bool) ([]byte, error) {
	return xxx_messageInfo_Process.Marshal(b, m, deterministic)
}
func (m *Process) XXX_Merge(src proto.Message) {
	xxx_messageInfo_Process.Merge(m, src)
}
func (m *Process) XXX_Size() int {
	return xxx_messageInfo_Process.Size(m)
}
func (m *Process) XXX_DiscardUnknown() {
	xxx_messageInfo_Process.DiscardUnknown(m)
}

var xxx_messageInfo_Process proto.InternalMessageInfo

func (m *Process) GetTime() string {
	if m != nil {
		return m.Time
	}
	return ""
}

func (m *Process) GetResult() uint32 {
	if m != nil {
		return m.Result
	}
	return 0
}

func init() {
	proto.RegisterType((*AddReceiptRequest)(nil), "inside_api.AddReceiptRequest")
	proto.RegisterType((*AddReceiptResponse)(nil), "inside_api.AddReceiptResponse")
	proto.RegisterType((*GetReceiptsRequest)(nil), "inside_api.GetReceiptsRequest")
	proto.RegisterType((*AddRawTicketRequest)(nil), "inside_api.AddRawTicketRequest")
	proto.RegisterType((*Receipt)(nil), "inside_api.Receipt")
	proto.RegisterType((*Item)(nil), "inside_api.Item")
	proto.RegisterType((*Document)(nil), "inside_api.Document")
	proto.RegisterType((*Ticket)(nil), "inside_api.Ticket")
	proto.RegisterType((*TicketDetails)(nil), "inside_api.TicketDetails")
	proto.RegisterType((*Query)(nil), "inside_api.Query")
	proto.RegisterType((*Organization)(nil), "inside_api.Organization")
	proto.RegisterType((*Seller)(nil), "inside_api.Seller")
	proto.RegisterType((*Operation)(nil), "inside_api.Operation")
	proto.RegisterType((*Process)(nil), "inside_api.Process")
}

func init() { proto.RegisterFile("receipts.proto", fileDescriptor_ef1c006035b8d22c) }

var fileDescriptor_ef1c006035b8d22c = []byte{
	// 1110 bytes of a gzipped FileDescriptorProto
	0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0xff, 0x8c, 0x56, 0x5d, 0x6f, 0x1b, 0x45,
	0x17, 0x96, 0xbf, 0xe3, 0xe3, 0x38, 0x4d, 0x26, 0x69, 0xde, 0x6d, 0xdf, 0x52, 0xcc, 0x0a, 0x15,
	0xab, 0x2a, 0x51, 0x70, 0x00, 0x15, 0xc4, 0x4d, 0x94, 0xb4, 0xc8, 0x20, 0x19, 0x67, 0x9c, 0x72,
	0x8b, 0x36, 0x3b, 0x93, 0x74, 0x88, 0xbd, 0xeb, 0xcc, 0xcc, 0x16, 0xc2, 0x6f, 0xe1, 0x9e, 0x3b,
	0xfe, 0x01, 0xff, 0x0d, 0x9d, 0x33, 0xb3, 0xf6, 0x6c, 0x1a, 0x04, 0x77, 0x73, 0x9e, 0xf3, 0x39,
	0xe7, 0x39, 0x7b, 0x66, 0x61, 0x4b, 0xcb, 0x54, 0xaa, 0xa5, 0x35, 0x07, 0x4b, 0x9d, 0xdb, 0x9c,
	0x81, 0xca, 0x8c, 0x12, 0xf2, 0xa7, 0x64, 0xa9, 0xe2, 0x31, 0xec, 0x1c, 0x0b, 0xc1, 0x9d, 0x01,
	0x97, 0x37, 0x85, 0x34, 0x96, 0xed, 0x43, 0xbb, 0x30, 0x52, 0x8f, 0x45, 0x54, 0x1b, 0xd4, 0x86,
	0x5d, 0xee, 0x25, 0xf6, 0x04, 0xba, 0x3e, 0xd4, 0x99, 0x8e, 0xea, 0xa4, 0x5a, 0x03, 0xf1, 0x73,
	0x60, 0x61, 0x28, 0xb3, 0xcc, 0x33, 0x23, 0xd9, 0x1e, 0xb4, 0xa4, 0xd6, 0xb9, 0xf6, 0xa1, 0x9c,
	0x10, 0xbf, 0x00, 0xf6, 0xad, 0xb4, 0xde, 0xd6, 0xfc, 0x4b, 0xde, 0xf8, 0x3b, 0xd8, 0xc5, 0xc8,
	0xc9, 0x2f, 0xe7, 0x2a, 0xbd, 0x96, 0xab, 0x32, 0x8f, 0xa0, 0x23, 0xa4, 0x4d, 0xd4, 0xdc, 0x90,
	0x7d, 0x6f, 0xf4, 0xe8, 0x60, 0x7d, 0xb3, 0x03, 0x67, 0x7b, 0xea, 0x0c, 0x78, 0x69, 0x19, 0xff,
	0xb5, 0x01, 0x1d, 0x9f, 0x97, 0x3d, 0x86, 0x8d, 0xd3, 0xc4, 0xca, 0x73, 0xb5, 0x90, 0x14, 0xa1,
	0xc1, 0x57, 0x32, 0x8b, 0x61, 0xf3, 0x24, 0x31, 0x6f, 0xcf, 0x73, 0x9b, 0xcc, 0x67, 0xc5, 0x82,
	0xae, 0xdb, 0xe0, 0x15, 0x8c, 0x31, 0x68, 0x9e, 0xe4, 0x42, 0x46, 0x8d, 0x41, 0x6d, 0xd8, 0xe2,
	0x74, 0xc6, 0x1e, 0x9d, 0x68, 0x29, 0x94, 0x45, 0xa7, 0x26, 0x39, 0xad, 0x01, 0xf6, 0x31, 0xf4,
	0x5f, 0xa5, 0x61, 0xd8, 0x16, 0x59, 0x54, 0x41, 0x36, 0x82, 0xbd, 0xd7, 0xca, 0xa4, 0xc9, 0xfc,
	0x34, 0x4f, 0x8b, 0x85, 0xcc, 0xec, 0xa4, 0x58, 0x5c, 0x48, 0x1d, 0xb5, 0x29, 0xcf, 0xbd, 0x3a,
	0xec, 0xdd, 0xeb, 0xcc, 0xbc, 0xd1, 0xf3, 0xa8, 0xeb, 0x7a, 0xe7, 0x24, 0xf6, 0x0c, 0x5a, 0x63,
	0x2b, 0x17, 0x26, 0x82, 0x41, 0x63, 0xd8, 0x1b, 0x6d, 0x87, 0x2d, 0x42, 0x05, 0x77, 0x6a, 0x16,
	0x41, 0xe7, 0x0d, 0x76, 0x3b, 0xcb, 0xa2, 0x1e, 0x05, 0x28, 0x45, 0x64, 0x70, 0x22, 0xcc, 0x67,
	0x87, 0xd1, 0x26, 0xd5, 0xea, 0x84, 0x12, 0x7d, 0x19, 0xf5, 0xd7, 0xe8, 0x4b, 0xbc, 0xdf, 0x0f,
	0x4b, 0xa9, 0x13, 0xab, 0xf2, 0xec, 0xfc, 0x76, 0x29, 0xa3, 0x2d, 0x2a, 0xb9, 0x0a, 0x62, 0xdf,
	0x1d, 0x90, 0xeb, 0xe8, 0x01, 0x25, 0x5b, 0xc9, 0xec, 0x29, 0xc0, 0x54, 0xcb, 0x65, 0xa2, 0x04,
	0xb6, 0x67, 0x9b, 0x82, 0x07, 0x08, 0xf2, 0x32, 0xd5, 0xf9, 0x3b, 0x65, 0x54, 0x9e, 0xa1, 0xc5,
	0x8e, 0xe3, 0x25, 0xc4, 0xb0, 0x0a, 0x3f, 0x23, 0xbe, 0x71, 0xcc, 0x55, 0x51, 0x01, 0xd9, 0x00,
	0x7a, 0x9c, 0x86, 0x62, 0x3a, 0x4f, 0x52, 0x19, 0xed, 0x52, 0x21, 0x21, 0xc4, 0x0e, 0x80, 0x05,
	0xe2, 0xb1, 0x10, 0x5a, 0x1a, 0x13, 0xed, 0x91, 0xe1, 0x3d, 0x1a, 0xbc, 0xd7, 0x8a, 0xd8, 0x87,
	0x6e, 0x9e, 0xc2, 0x59, 0xc1, 0x86, 0x46, 0xfb, 0xe4, 0x4d, 0x67, 0xf6, 0x0c, 0xb6, 0xa6, 0xb9,
	0xb1, 0xcb, 0xe4, 0x16, 0x89, 0x44, 0xaf, 0xff, 0x0d, 0x6a, 0xc3, 0x26, 0xbf, 0x83, 0xe2, 0x3c,
	0x9c, 0xe4, 0x45, 0x66, 0xa5, 0x9e, 0x15, 0x17, 0x0b, 0x65, 0xca, 0xbb, 0x47, 0x64, 0x7d, 0xaf,
	0x8e, 0xbd, 0x80, 0x1d, 0x3f, 0x27, 0x5a, 0xbd, 0x93, 0xbe, 0x0f, 0x8f, 0x28, 0xf9, 0xfb, 0x0a,
	0xec, 0xba, 0x03, 0x67, 0xea, 0x2a, 0x8b, 0x1e, 0x0f, 0x6a, 0xc3, 0x3e, 0x0f, 0x10, 0xbc, 0xd9,
	0xf7, 0xd7, 0x96, 0xcb, 0xab, 0xb1, 0x88, 0xfe, 0xef, 0x18, 0x2b, 0x65, 0xec, 0x36, 0xf1, 0xb3,
	0xba, 0xc4, 0x13, 0x2a, 0xab, 0x0a, 0xb2, 0x21, 0x3c, 0x98, 0xe2, 0xf6, 0x49, 0xf3, 0xf9, 0x8f,
	0x52, 0x63, 0x95, 0xd1, 0x07, 0x94, 0xe6, 0x2e, 0xec, 0x78, 0xa1, 0x0f, 0x94, 0x3e, 0xae, 0xa7,
	0x64, 0x15, 0x42, 0x98, 0x71, 0x26, 0x33, 0x21, 0x75, 0x49, 0xc9, 0x87, 0x54, 0x52, 0x15, 0xc4,
	0x38, 0xb3, 0xb7, 0xea, 0xb2, 0x9c, 0x81, 0x81, 0x8b, 0x13, 0x40, 0x38, 0x4b, 0xe7, 0xc9, 0xaf,
	0xeb, 0x61, 0xfd, 0x88, 0x4c, 0x2a, 0x18, 0xdb, 0x82, 0xfa, 0x58, 0x44, 0x31, 0x25, 0xa8, 0x8f,
	0x45, 0xfc, 0x67, 0x1d, 0x9a, 0xf8, 0xc5, 0x20, 0xa1, 0x93, 0xc4, 0x2f, 0x8e, 0x2e, 0xa7, 0x33,
	0xdb, 0x86, 0xc6, 0x44, 0x18, 0xda, 0x15, 0x2d, 0x8e, 0x47, 0xfc, 0x2c, 0x27, 0xc2, 0x60, 0x57,
	0x1a, 0x34, 0x10, 0x5e, 0xc2, 0xe2, 0xa6, 0xae, 0x39, 0x94, 0xb9, 0x49, 0x1e, 0x21, 0x84, 0x1f,
	0xd8, 0x54, 0xab, 0x54, 0xfa, 0x15, 0xe1, 0x04, 0x24, 0xe2, 0xac, 0x48, 0x32, 0xab, 0xec, 0x2d,
	0xad, 0x83, 0x3a, 0x5f, 0xc9, 0x98, 0x1d, 0x13, 0x75, 0xc8, 0x1e, 0x8f, 0xec, 0x4b, 0xd8, 0x3f,
	0x49, 0xe6, 0x69, 0x31, 0xa7, 0xfb, 0xcc, 0x8a, 0x8b, 0x9f, 0x65, 0x6a, 0x89, 0xe2, 0x0d, 0xba,
	0xea, 0x3f, 0x68, 0xd9, 0x21, 0xec, 0x06, 0x1a, 0x2c, 0x87, 0x9c, 0xba, 0xe4, 0x74, 0x9f, 0x0a,
	0xd7, 0xc7, 0x44, 0x18, 0x9e, 0x58, 0x19, 0x01, 0x59, 0x95, 0x62, 0xfc, 0x15, 0x6c, 0x94, 0xab,
	0x8a, 0x7d, 0x0a, 0x1d, 0xff, 0x5e, 0xf8, 0x8d, 0xbd, 0x1b, 0xae, 0xa3, 0xf2, 0xe9, 0x28, 0x6d,
	0xe2, 0xaf, 0xa1, 0xed, 0xb6, 0x38, 0x3b, 0x84, 0x0d, 0xe1, 0x83, 0x78, 0xcf, 0xbd, 0xd0, 0xb3,
	0x4c, 0xc0, 0x57, 0x56, 0xf1, 0xef, 0x0d, 0xe8, 0x57, 0x9e, 0x00, 0xa4, 0xc2, 0xd8, 0xc4, 0x16,
	0xee, 0xb5, 0xe8, 0x73, 0x2f, 0x21, 0xc3, 0x4a, 0xf8, 0xe7, 0xac, 0xae, 0x04, 0x12, 0x7b, 0xad,
	0x32, 0x41, 0x84, 0x75, 0x39, 0x9d, 0x71, 0xab, 0xa7, 0x5a, 0x26, 0x56, 0x8a, 0x63, 0x4b, 0x64,
	0x75, 0xf9, 0x1a, 0xc0, 0x08, 0x37, 0x9a, 0x78, 0xea, 0xf2, 0xfa, 0x8d, 0x66, 0x47, 0xd0, 0xcd,
	0xcb, 0x85, 0x47, 0x2c, 0xf5, 0x46, 0x0f, 0xc3, 0x72, 0x57, 0xdb, 0x90, 0xaf, 0xed, 0xb0, 0x37,
	0x4b, 0x9d, 0xa7, 0x38, 0xce, 0x1d, 0x5a, 0xd5, 0x95, 0xde, 0x4c, 0x9d, 0x8a, 0x97, 0x36, 0xec,
	0x13, 0x68, 0xdd, 0x14, 0x52, 0xdf, 0x12, 0x93, 0xbd, 0xd1, 0x4e, 0x68, 0x7c, 0x86, 0x0a, 0xee,
	0xf4, 0xec, 0x39, 0xb4, 0x2d, 0xf5, 0x81, 0xe8, 0xeb, 0x8d, 0xd8, 0xfb, 0x8f, 0x24, 0xf7, 0x16,
	0xec, 0x1b, 0xd8, 0xcc, 0xf5, 0x55, 0x92, 0xa9, 0xdf, 0x5c, 0xed, 0x40, 0x1e, 0x51, 0xa5, 0xf6,
	0x40, 0xcf, 0x2b, 0xd6, 0x98, 0xc9, 0xc8, 0xf9, 0x5c, 0x6a, 0x7a, 0x41, 0xee, 0x64, 0x9a, 0x91,
	0x86, 0x7b, 0x8b, 0xf8, 0x8f, 0x1a, 0xb4, 0xa8, 0x4c, 0xfc, 0x98, 0xf3, 0xca, 0x93, 0xe1, 0xd8,
	0xa9, 0x82, 0x38, 0xdb, 0xc6, 0xbf, 0xc2, 0x4d, 0x8e, 0x47, 0x5c, 0x59, 0x25, 0xd9, 0x63, 0x47,
	0x56, 0x9f, 0x07, 0x08, 0xd2, 0x78, 0x69, 0xc6, 0xc2, 0xb3, 0x45, 0x67, 0xf4, 0xb9, 0x5c, 0xaf,
	0x39, 0x47, 0x58, 0x80, 0xa0, 0x8f, 0xc0, 0x11, 0x6e, 0x3b, 0x1f, 0x3c, 0xc7, 0x9f, 0xc3, 0x66,
	0x78, 0x67, 0xb4, 0xc9, 0x82, 0xef, 0x3e, 0xf3, 0xdf, 0xbd, 0xca, 0x32, 0x3f, 0x43, 0x78, 0x8c,
	0x0f, 0xa0, 0xed, 0x6e, 0xfc, 0x1f, 0xed, 0x5f, 0x41, 0x77, 0x35, 0x15, 0xab, 0x32, 0x6a, 0xeb,
	0x32, 0x10, 0xb3, 0xd8, 0x9d, 0x3a, 0x5d, 0x94, 0xce, 0x65, 0x53, 0x1a, 0xab, 0xa6, 0xc4, 0x5f,
	0x40, 0xc7, 0x4f, 0x0a, 0x39, 0xa8, 0x75, 0x5e, 0x3c, 0xe3, 0x27, 0xa0, 0xa5, 0x29, 0xe6, 0xd6,
	0x87, 0xf1, 0xd2, 0x45, 0x9b, 0x7e, 0x0c, 0x8f, 0xfe, 0x0e, 0x00, 0x00, 0xff, 0xff, 0xde, 0xb0,
	0x0c, 0xb6, 0x2a, 0x0a, 0x00, 0x00,
}
