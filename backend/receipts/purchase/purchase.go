package purchase

//Purchase is one line from shopping list.
type Purchase struct {
	Price      int32      `json:"price"`
	Sum        int32      `json:"sum"`
	Quantity   float32    `json:"quantity"`
	Name       string     `json:"name"`
	Categories []Category `json:"categories"`
}

//Category - purchase category.
type Category string

const (
	Food          Category = "food"
	Alcohol       Category = "alcohol"
	Clothes       Category = "clothes"
	Shoes         Category = "shoes"
	Medicine      Category = "medicine"
	HomeAppliance Category = "home_appliance"
	Entertainment Category = "entertainment"
)
