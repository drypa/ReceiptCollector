package render

import (
	"bytes"
	"html/template"
	"log"
	"os"
	"path/filepath"
	"receipt_collector/nalogru"
)

// Render receipt as HTML
func Render(receipt *nalogru.Receipt) ([]byte, error) {
	wd, err := os.Getwd()
	if err != nil {
		return nil, err
	}
	path := filepath.Join(wd, "backend/render/templates/receipt.html")
	tmpl, err := template.New("receipt.html").Funcs(template.FuncMap{"money": money}).ParseFiles(path)

	if err != nil {
		return nil, err
	}

	var tpl bytes.Buffer
	err = tmpl.Execute(&tpl, receipt)
	if err != nil {
		return nil, err
	}
	log.Println(tpl.String())
	return tpl.Bytes(), nil
}

func money(m int64) float64 {
	return float64(m) / 100
}
