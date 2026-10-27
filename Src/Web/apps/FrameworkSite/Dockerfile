# syntax=docker/dockerfile:1.27.1@sha256:4edf897a3ffa55b89f906fc8cc78afdb3f1834cc9c7083565e611a8a7d5fe99e

# Built from Src/Web once web-scaffold has created the app: docker build -f apps/FrameworkSite/Dockerfile Src/Web
FROM node:24.22.1-alpine3.24@sha256:06d7adf3e0ef61a13f1d28682726366f97a15aa1888c934d3ecec86c33918b53 AS build

ENV CI=true \
#if (webAngular)
    NG_CLI_ANALYTICS=false \
#endif
    COREPACK_ENABLE_DOWNLOAD_PROMPT=0

WORKDIR /workspace

COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN corepack enable && pnpm fetch

COPY . .
RUN pnpm install --offline --frozen-lockfile --filter framework-site... \
    && pnpm --filter framework-site build
#if (webReactRouter)

# React Router's page starts the app from inline scripts: the policy allows each by its SHA-256, and no other.
RUN node --input-type=module <<'HASHES'
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';

const page = readFileSync('apps/FrameworkSite/dist/client/index.html', 'utf8');
const hashes = [...page.matchAll(/<script\b(?![^>]*\ssrc\s*=)[^>]*>([\s\S]*?)<\/script\s*>/gi)]
    .map(([, script]) => `'sha256-${createHash('sha256').update(script, 'utf8').digest('base64')}'`);
const server = readFileSync('apps/FrameworkSite/nginx.conf', 'utf8');

if (!server.includes("script-src 'self';")) {
    throw new Error("apps/FrameworkSite/nginx.conf has no script-src 'self'; for the hashes to join.");
}

writeFileSync('default.conf.template', server.replace("script-src 'self';", `script-src ${["'self'", ...hashes].join(' ')};`));
HASHES
#endif

FROM nginxinc/nginx-unprivileged:1.31.7-alpine3.24@sha256:b5047b9b76845eac124358373c6852377c52e4b05834a47647e955dd91cb8f1e AS runtime

ENV API_UPSTREAM=http://api:8080 \
    NGINX_ENTRYPOINT_LOCAL_RESOLVERS=1

#if (webReactRouter)
COPY --from=build /workspace/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /workspace/apps/FrameworkSite/dist/client/ /usr/share/nginx/html/
#endif
#if (!webReactRouter)
COPY apps/FrameworkSite/nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=build /workspace/apps/FrameworkSite/dist/ /usr/share/nginx/html/
#endif

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=5 \
    CMD wget --spider --quiet http://127.0.0.1:8080/ || exit 1
