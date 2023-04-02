package main

import (
	"fmt"
	"math/rand"
	"net/http"
	"os"
)

func main() {
	baseDir := "./dump/"
	files := getFiles(baseDir)
	http.HandleFunc("/", func(writer http.ResponseWriter, request *http.Request) {
		fmt.Println(request.URL.String())
		if request.Method != http.MethodGet {
			writer.WriteHeader(http.StatusNotFound)
			return
		}

		fileNumber := rand.Intn(len(files))
		randomFile := files[fileNumber]
		content, err := os.ReadFile(baseDir + randomFile.Name())
		check(err)
		_, _ = writer.Write(content)

	})

	fmt.Println(http.ListenAndServe(":9999", nil))
}

func getFiles(directory string) []os.DirEntry {
	files, err := os.ReadDir(directory)
	check(err)

	return files
}

func check(err error) {
	if err != nil {
		panic(err)
	}
}
