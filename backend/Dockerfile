#build stage
FROM golang:alpine AS build
COPY . /src
WORKDIR /src
RUN CGO_ENABLED=0 go build -o receipt_collector

#final
FROM alpine
WORKDIR /app
COPY --from=build /src/receipt_collector /app/
ENTRYPOINT ./receipt_collector


