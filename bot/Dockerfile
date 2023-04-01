#build stage
FROM golang:alpine3.17 AS build
COPY . /src
WORKDIR /src
RUN CGO_ENABLED=0 go build -o bot

#final
FROM alpine:3.17.3
WORKDIR /app
COPY --from=build /src/bot /app/
ENTRYPOINT ./bot
