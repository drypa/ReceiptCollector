#build stage
FROM golang:1.27-alpine AS build
COPY . /src
WORKDIR /src/bot
RUN CGO_ENABLED=0 go build -o bot

#final
FROM alpine:3.19.1
WORKDIR /app
COPY --from=build /src/bot/bot /app/
ENTRYPOINT ./bot
