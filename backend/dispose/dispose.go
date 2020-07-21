package dispose

import "log"

type deferFunc func() error

//Dispose provides method to defer func returned error.
func Dispose(fun deferFunc, errorMessage string) {
	err := fun()
	if err != nil {
		log.Printf(errorMessage, err)
	}

}
