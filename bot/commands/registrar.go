package commands

type Registrar struct {
	commands       []Command
	defaultCommand Command
}

func NewRegistrar() *Registrar {
	registrar := Registrar{
		commands: []Command{},
	}
	return &registrar
}

func (r *Registrar) Register(c Command) {
	r.commands = append(r.commands, c)
}

func (r *Registrar) RegisterDefault(c Command) {
	r.defaultCommand = c
}

func (r *Registrar) Get(message string) *Command {
	for _, c := range r.commands {
		if c.Accepted(message) {
			return &c
		}
	}
	return &r.defaultCommand
}
