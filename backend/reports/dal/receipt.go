package dal

import "go.mongodb.org/mongo-driver/bson/primitive"

type Receipt struct {
	Id          primitive.ObjectID `bson:"_id,omitempty" json:"id"`
	Owner       primitive.ObjectID `json:"owner" bson:"owner"`
	QueryString string             `bson:"query_string" json:"queryString"`
}
