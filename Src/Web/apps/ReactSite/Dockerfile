# syntax=docker/dockerfile:1.27.1@sha256:4edf897a3ffa55b89f906fc8cc78afdb3f1834cc9c7083565e611a8a7d5fe99e

# Built from Src/Web once web-scaffold has created the app: docker build -f apps/ReactSite/Dockerfile Src/Web
FROM node:24.18.0-alpine3.24@sha256:a0b9bf06e4e6193cf7a0f58816cc935ff8c2a908f81e6f1a95432d679c54fbfd AS build

ENV CI=true \
    COREPACK_ENABLE_DOWNLOAD_PROMPT=0

WORKDIR /workspace

COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN corepack enable && pnpm fetch

COPY . .
RUN pnpm install --offline --frozen-lockfile --filter react-site... \
    && pnpm --filter react-site build

FROM nginxinc/nginx-unprivileged:1.30.6-alpine3.24@sha256:f5116ed4606f3eb015dd4a8e28cb9b351476dc0ad4bc6e9f93b19cec130cdf03 AS runtime

ENV API_UPSTREAM=http://api:8080 \
    NGINX_ENTRYPOINT_LOCAL_RESOLVERS=1

COPY apps/ReactSite/nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=build /workspace/apps/ReactSite/dist/ /usr/share/nginx/html/

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=5 \
    CMD wget --spider --quiet http://127.0.0.1:8080/ || exit 1
