# AjoCircle web app: the built React site, served by nginx, which also forwards /api to the API container so the
# session cookie stays same-origin (SameSite=Strict, no CORS). Build from the repository root:
#   docker build -f deploy/web.Dockerfile -t ajocircle-web .
FROM node:24-alpine AS build
WORKDIR /web
COPY ajocircle-web/package.json ajocircle-web/package-lock.json ./
RUN npm ci
COPY ajocircle-web/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.27-alpine
COPY deploy/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist /usr/share/nginx/html
EXPOSE 8080
