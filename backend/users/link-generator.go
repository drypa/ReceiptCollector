package users

//LinkGenerator is interface that allow to generate unique link for user.
type LinkGenerator interface {
	GetRedirectLink(userId string) (string, error)
}
