package render

import (
	"bytes"
	"html/template"
	"log"
	"path/filepath"
	"receipt_collector/nalogru"
)

// Render used to make report from templates
type Render struct {
	templatesPath string
}

// New create Render
func New(templatesPath string) *Render {
	return &Render{templatesPath: templatesPath}
}

// Receipt render receipt as HTML
func (r *Render) Receipt(receipt *nalogru.Receipt) ([]byte, error) {
	file := "receipt.html"
	path := filepath.Join(r.templatesPath, file)
	tmpl, err := template.New(file).Funcs(template.FuncMap{"money": money}).ParseFiles(path)

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
