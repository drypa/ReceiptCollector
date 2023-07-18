package commands

import "testing"

func TestAccepted_RegisterCommand_Success(t *testing.T) {
	command := NewRegisterCommand(nil)

	variants := make([]string, 0)
	variants = append(variants, "/register +1234567890")
	variants = append(variants, "/register	+0987654321")
	variants = append(variants, "/register    +0987654321")
	variants = append(variants, "/register 	 +0987654321")

	for _, s := range variants {
		res := command.Accepted(s)
		if !res {
			t.Fatalf("message '%s' is not acceptable for RegisterCommand", s)
		}
	}

}

func TestAccepted_RegisterCommand_Failed(t *testing.T) {
	command := NewRegisterCommand(nil)

	variants := make([]string, 0)
	variants = append(variants, "")
	variants = append(variants, " ")
	variants = append(variants, "	")
	variants = append(variants, "/register")
	variants = append(variants, "/register+1234567890")
	variants = append(variants, "	/register +1234567890 ")
	variants = append(variants, "/register +")
	variants = append(variants, "/register +1")
	variants = append(variants, "/register +12345678901")
	variants = append(variants, "/register +abcdefghjk")

	for _, s := range variants {
		res := command.Accepted(s)
		if res {
			t.Fatalf("message '%s' is accepted for RegisterCommand", s)
		}
	}

}
