package dispose

import "log"

type deferFunc func() error

func Dispose(fun deferFunc, errorMessage string) {
	err := fun()
	if err != nil {
		log.Printf(errorMessage, err)
	}

}
