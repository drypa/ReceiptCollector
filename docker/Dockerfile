FROM alpine:3.17.3
RUN apk -U --no-cache add ca-certificates

COPY receipt_collector /app/receipt_collector

EXPOSE 8888

CMD ["/app/receipt_collector"]
