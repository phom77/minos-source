FROM node:24.15.0-slim AS build
WORKDIR /web
COPY web/minos-web/package.json web/minos-web/package-lock.json ./
RUN npm ci
COPY web/minos-web/ ./
RUN npm run build

FROM nginx:1.28.2-alpine
COPY --from=build /web/dist/minos-web/browser/ /usr/share/nginx/html/
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY docker/proxy-api.conf /etc/nginx/proxy-api.conf
EXPOSE 8443
