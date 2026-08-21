# 1. Nginx Proxy with TLS Termination for Frontend and Analytics API

## Status

Accepted

## Context

The ReceiptCollector system requires a reverse proxy to serve frontend static assets and proxy requests to the Analytics API service. The system should also terminate TLS connections at the nginx layer before forwarding requests to backend services.

Current architecture uses Docker Compose with three main services:
- Backend (Go): HTTP + gRPC endpoints
- Telegram Bot (Go): gRPC client to backend 
- Analytics (.NET 8): Migrates receipts from MongoDB → PostgreSQL, provides analytics UI

The existing nginx configuration is incomplete and needs enhancement for proper TLS termination and frontend asset serving.

## Decision

We will implement a comprehensive Nginx configuration that:
1. Serves static assets (frontend) directly
2. Proxies API requests to the Analytics service
3. Terminates TLS connections 
4. Maintains proper HTTPS redirects and security headers

The nginx proxy will be deployed as part of the docker-compose stack in production.

## Consequences

### Positive Effects

- Centralized reverse proxy for all frontend and API traffic
- Proper TLS termination at the edge of the system
- Simplified service architecture with single point of entry
- Improved security through proper HTTPS handling
- Better performance with static asset caching
- Clear separation between frontend assets and backend APIs

### Negative Effects

- Additional complexity in the nginx configuration management
- Need for certificate management (SSL certificates)
- Potential bottleneck at the proxy layer if not properly scaled
- Increased deployment complexity due to additional service

## Implementation Details

The nginx configuration will:
1. Listen on ports 80 and 443 for HTTP/HTTPS traffic
2. Serve static assets from the Analytics frontend directly 
3. Proxy API requests (URLs starting with `/api`) to analytics service at `http://analytics:5039`
4. Provide proper HTTPS redirects from HTTP to HTTPS
5. Implement security headers and appropriate caching

## Configuration Structure

### Nginx Server Configuration
- Listen on port 80 for HTTP requests 
- Redirect all HTTP traffic to HTTPS
- Listen on port 443 for HTTPS with TLS certificates
- Configure SSL/TLS settings according to modern best practices
- Proxy API calls to backend services through proper upstream configurations

### Security Considerations
- Implement HSTS header
- Set secure headers (X-Content-Type-Options, X-Frame-Options, etc.)
- Use appropriate TLS protocols and cipher suites
- Enable HTTP/2 support where applicable

## Alternatives Considered

1. **No nginx proxy**: Direct access to services - increased complexity for service discovery and routing
2. **Multiple reverse proxies**: Separate instances for frontend vs API - more complex management 
3. **Service mesh approach (Istio/Kuma)**: Overkill for current requirements with additional operational complexity
4. **Internal load balancer only**: Would require modifying all services to handle external traffic

## References

- Docker Compose service definitions in `docker-compose.yml`
- Analytics service port information from `.env` 
- Existing nginx config at `nginx.conf`