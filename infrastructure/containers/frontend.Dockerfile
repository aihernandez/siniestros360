ARG NODE_IMAGE
ARG NGINX_IMAGE
FROM ${NODE_IMAGE} AS build
ARG APP
WORKDIR /repo/apps/${APP}
COPY apps/${APP}/package*.json ./
RUN npm ci
COPY apps/${APP}/ .
# Preserve the sibling stylesheet imported by adjuster-app.
COPY apps/insured-app/src/style.css /repo/apps/insured-app/src/style.css
ENV VITE_GATEWAY_URL=""
RUN npm run build

FROM ${NGINX_IMAGE}
ARG APP
COPY infrastructure/containers/spa.conf /etc/nginx/conf.d/default.conf
COPY --from=build /repo/apps/${APP}/dist/ /usr/share/nginx/html/
USER 101
EXPOSE 8080
ENTRYPOINT ["nginx", "-g", "daemon off;"]
