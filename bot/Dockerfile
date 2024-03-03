#build stage
FROM golang:alpine3.19 AS build
COPY . /src
WORKDIR /src
RUN CGO_ENABLED=0 go build -o bot

#final
FROM alpine:3.19.1
WORKDIR /app
COPY --from=build /src/bot /app/
ENTRYPOINT ./bot
