package utils

import "fmt"

type deferFunc func() error

func Dispose(fun deferFunc, errorMessage string) {
	err := fun()
	if err != nil {
		fmt.Printf(errorMessage, err)
	}

}
