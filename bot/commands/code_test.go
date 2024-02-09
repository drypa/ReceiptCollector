package commands

import "testing"

func TestAccepted_ConfirmationCodeCommand_Success(t *testing.T) {
	command := NewConfirmationCodeCommand(nil, nil)

	variants := make([]string, 0)
	variants = append(variants, "/code 1234")
	variants = append(variants, "/code	9876")
	variants = append(variants, "/code    0000")
	variants = append(variants, "/code 	 0001")

	for _, s := range variants {
		res := command.Accepted(s)
		if !res {
			t.Fatalf("message '%s' is not acceptable for ConfirmationCodeCommand", s)
		}
	}
}

func TestAccepted_ConfirmationCodeCommand_Failed(t *testing.T) {
	command := NewConfirmationCodeCommand(nil, nil)

	variants := make([]string, 0)
	variants = append(variants, "")
	variants = append(variants, " ")
	variants = append(variants, "	")
	variants = append(variants, "/code")
	variants = append(variants, "code 1234")
	variants = append(variants, "/code1234")
	variants = append(variants, "	/code 1234 ")
	variants = append(variants, "/code +")
	variants = append(variants, "/code ++++")
	variants = append(variants, "/code dddd")
	variants = append(variants, "/code 1")
	variants = append(variants, "/code 12")
	variants = append(variants, "/code 123")
	variants = append(variants, "/code 12345")
	variants = append(variants, "/code +.*")

	for _, s := range variants {
		res := command.Accepted(s)
		if res {
			t.Fatalf("message '%s' is accepted for RegisterCommand", s)
		}
	}
}

func Test_getCodeFromRequest_Success(t *testing.T) {
	command := NewConfirmationCodeCommand(nil, nil)

	message := "/code 0101"

	phone := command.getCodeFromRequest(message)
	expected := "0101"
	if phone != expected {
		t.Fatalf("wrong code returned from getCodeFromRequest. Expeced '%s' but got '%s'", expected, phone)
	}
}
