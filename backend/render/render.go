package render

import (
	"bytes"
	"html/template"
	"log"
	"path/filepath"
	"receipt_collector/nalogru"
	"time"
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
	tmpl, err := template.New(file).Funcs(
		template.FuncMap{
			"money":    money,
			"unixDate": unixDate,
		}).ParseFiles(path)

	if err != nil {
		log.Printf("failed to load receipt template %v\n", err)
		return nil, err
	}

	var tpl bytes.Buffer
	err = tmpl.Execute(&tpl, receipt)
	if err != nil {
		log.Printf("failed to render receipt %v\n", err)
		return nil, err
	}
	log.Println(tpl.String())
	return tpl.Bytes(), nil
}

func money(m int64) float64 {
	return float64(m) / 100
}

func unixDate(dateTime int64) string {
	return time.Unix(dateTime, 0).Format("02.01.2006 15:04:05")
}
