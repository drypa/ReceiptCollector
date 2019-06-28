FROM nginx:1.16.0-alpine
COPY nginx.conf /etc/nginx/nginx.conf

# Remove default nginx website
RUN rm -rf /usr/share/nginx/html

COPY ./dist /var/www/html
