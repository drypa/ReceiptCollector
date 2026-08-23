#build stage
FROM golang:1.27-alpine AS build
COPY . /src
WORKDIR /src/backend
RUN CGO_ENABLED=0 go build -o receipt_collector

#final
FROM alpine:3.19.1
WORKDIR /app
COPY --from=build /src/backend/receipt_collector /app/
ENTRYPOINT ./receipt_collector


