package commands

import "testing"

func TestAccepted_RegisterCommand_Success(t *testing.T) {
	command := NewRegisterCommand(nil, nil)

	variants := make([]string, 0)
	variants = append(variants, "/register +71234567890")
	variants = append(variants, "/register	+70987654321")
	variants = append(variants, "/register    +00987654321")
	variants = append(variants, "/register 	 +00987654321")

	for _, s := range variants {
		res := command.Accepted(s)
		if !res {
			t.Fatalf("message '%s' is not acceptable for RegisterCommand", s)
		}
	}

}

func TestAccepted_RegisterCommand_Failed(t *testing.T) {
	command := NewRegisterCommand(nil, nil)

	variants := make([]string, 0)
	variants = append(variants, "")
	variants = append(variants, " ")
	variants = append(variants, "	")
	variants = append(variants, "/register")
	variants = append(variants, "register +71234567890")
	variants = append(variants, "/register+71234567890")
	variants = append(variants, "	/register +11234567890 ")
	variants = append(variants, "/register +")
	variants = append(variants, "/register +1")
	variants = append(variants, "/register +012345678901")
	variants = append(variants, "/register +abcdefghjk")
	variants = append(variants, "/register ++++++++++++")
	variants = append(variants, "/register +.*")

	for _, s := range variants {
		res := command.Accepted(s)
		if res {
			t.Fatalf("message '%s' is accepted for RegisterCommand", s)
		}
	}
}

func Test_getPhoneFromRequest_Success(t *testing.T) {
	command := NewRegisterCommand(nil, nil)

	message := "/register +71234567890"

	phone := command.getPhoneFromRequest(message)
	expected := "71234567890"
	if phone != expected {
		t.Fatalf("wrong phone returned from getPhoneFromRequest. Expeced '%s' but got '%s'", expected, phone)
	}
}
