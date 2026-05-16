# ReceiptCollector
![](https://github.com/drypa/ReceiptCollector/workflows/Docker%20Image%20CI/badge.svg)
[![Go Report Card](https://goreportcard.com/badge/github.com/drypa/ReceiptCollector)](https://goreportcard.com/report/github.com/drypa/ReceiptCollector)

Russian Tax service provides mobile application "Проверка чека" to get receipt information online.
ReceiptCollector uses nalog.ru api to collect purchase data.


### how to build
```bash
sudo chmod +x ./build.sh 
./build.sh
```

### how to run
```bash
sudo chmod +x ./up.sh 
./up.sh
```

### how to stop
```bash
sudo chmod +x ./down.sh 
./down.sh
```

### Nginx Proxy Configuration
The system uses nginx as a reverse proxy for:
- Serving frontend static assets  
- Proxying API requests to Analytics service
- Terminating TLS connections

Nginx is configured with proper SSL certificates and security headers.

### Development Environment Setup
For development, all services are proxied through Nginx. The analytics service will be available at:

- API: http://localhost/api/
- Frontend: http://localhost/

To run with the new Nginx proxy:
```bash
./up.dev.sh
```

### Analytics Service (.NET 10)
The analytics service has been migrated to .NET 10. To run it locally:

```bash
# Migrate database first
cd ReceiptCollector.Analytics.Migrations && dotnet run

# Then run the API
cd ReceiptCollector.Analytics.Api && dotnet run
```

### Useful scripts

```javascript
//reset status to allow workers reprocess it.
db.getCollection('receipt_requests').updateMany({check_request_status: 'requested'}, {$set: {check_request_status: 'undefined'}})
//or
db.getCollection('receipt_requests').updateMany({check_request_status: 'error'}, {$set: {check_request_status: 'undefined'}})

```

```javascript
//remove obsolete fields.
db.getCollection('receipt_requests').updateMany({}, {$unset: {odfs_request_status: '', odfs_requested: ''}})
```

```javascript
//refresh session manually
db.getCollection('devices').updateOne({"_id": ObjectId("000000000000000000000000")}, {
    "$set": {
        "session_id": "XXX:XXX",
        "refresh_token": "XXX"
    }
})
```

```javascript
//reset receipts error status
db.receipt_requests.updateMany({
    "query_string": /t=2024/,
    "check_request_status": "error"
}, {$set: {"check_request_status": null}}, {})
```

### SSL Certificate Generation

To generate SSL certificates for development:

```bash
chmod +x ./generate-ssl-cert.sh
./generate-ssl-cert.sh
```
