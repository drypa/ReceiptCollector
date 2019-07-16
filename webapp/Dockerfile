FROM node:lts-alpine as builder
WORKDIR /app

COPY ./ ./

RUN npm i && npm run build-prod



FROM nginx:1.16.0-alpine

# Remove default nginx website
RUN rm -rf /usr/share/nginx/html

COPY nginx.conf /etc/nginx/nginx.conf


COPY --from=builder  /app/dist /var/www/html

CMD ["nginx", "-g", "daemon off;"]
