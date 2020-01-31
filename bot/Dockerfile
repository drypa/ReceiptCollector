#build stage
FROM golang:alpine AS build
COPY . /src
WORKDIR /src
RUN go build -o bot

#final
FROM alpine
WORKDIR /app
COPY --from=build /src/bot /app/
ENTRYPOINT ./bot
