ARG NODE_IMAGE
ARG NGINX_IMAGE
FROM ${NODE_IMAGE} AS build
ARG APP
WORKDIR /app
COPY apps/${APP}/package*.json ./
RUN npm ci
COPY apps/${APP}/ .
ENV VITE_GATEWAY_URL=""
RUN npm run build

FROM ${NGINX_IMAGE}
COPY infrastructure/containers/spa.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/ /usr/share/nginx/html/
USER 101
EXPOSE 8080
ENTRYPOINT ["nginx", "-g", "daemon off;"]
