# syntax=docker/dockerfile:1.27.1@sha256:4edf897a3ffa55b89f906fc8cc78afdb3f1834cc9c7083565e611a8a7d5fe99e

# Built from Src/Web: docker build -f apps/PlaceholderSite/Dockerfile Src/Web
FROM nginxinc/nginx-unprivileged:1.31.7-alpine3.24@sha256:b5047b9b76845eac124358373c6852377c52e4b05834a47647e955dd91cb8f1e AS runtime

ENV API_UPSTREAM=http://api:8080 \
    NGINX_ENTRYPOINT_LOCAL_RESOLVERS=1

COPY apps/PlaceholderSite/nginx.conf /etc/nginx/templates/default.conf.template
COPY apps/PlaceholderSite/public/ /usr/share/nginx/html/

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=5 \
    CMD wget --spider --quiet http://127.0.0.1:8080/ || exit 1
