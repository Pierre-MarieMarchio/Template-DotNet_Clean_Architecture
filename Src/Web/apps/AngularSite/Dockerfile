# syntax=docker/dockerfile:1.27.1@sha256:4edf897a3ffa55b89f906fc8cc78afdb3f1834cc9c7083565e611a8a7d5fe99e

# Built from Src/Web once web-scaffold has created the app: docker build -f apps/AngularSite/Dockerfile Src/Web
FROM node:24.22.1-alpine3.24@sha256:06d7adf3e0ef61a13f1d28682726366f97a15aa1888c934d3ecec86c33918b53 AS build

ENV CI=true \
    NG_CLI_ANALYTICS=false \
    COREPACK_ENABLE_DOWNLOAD_PROMPT=0

WORKDIR /workspace

COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN corepack enable && pnpm fetch

COPY . .
RUN pnpm install --offline --frozen-lockfile --filter angular-site... \
    && pnpm --filter angular-site build

FROM nginxinc/nginx-unprivileged:1.31.7-alpine3.24@sha256:b5047b9b76845eac124358373c6852377c52e4b05834a47647e955dd91cb8f1e AS runtime

ENV API_UPSTREAM=http://api:8080 \
    NGINX_ENTRYPOINT_LOCAL_RESOLVERS=1

COPY apps/AngularSite/nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=build /workspace/apps/AngularSite/dist/angular-site/browser/ /usr/share/nginx/html/

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=5 \
    CMD wget --spider --quiet http://127.0.0.1:8080/ || exit 1
