FROM node:22-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.30.4-alpine3.24@sha256:93722936b82ec8a1178d48448e619226680d2de3706a1640800e186cd5fa7fd3 AS runtime
USER root
RUN apk add --no-cache --upgrade 'libcrypto3>=3.5.8-r0' 'libssl3>=3.5.8-r0'
COPY --chown=101:101 --from=build /app/dist /usr/share/nginx/html
COPY --chmod=0555 docker-entrypoint.d/40-dls-runtime-config.sh /docker-entrypoint.d/40-dls-runtime-config.sh
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY runtime-config/config.js.template /usr/share/nginx/templates/config.js.template
ENV NGINX_ENVSUBST_FILTER=^DLS_
USER 101
EXPOSE 8080
