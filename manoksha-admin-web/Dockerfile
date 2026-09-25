# Build from the repository root: docker build -f manoksha-admin-web/Dockerfile .
FROM node:22-alpine AS deps
WORKDIR /repo
COPY package.json package-lock.json ./
COPY packages/api-client/package.json packages/api-client/
COPY manoksha-admin-web/package.json manoksha-admin-web/
COPY manoksha-customer-web/package.json manoksha-customer-web/
RUN npm ci

FROM node:22-alpine AS build
WORKDIR /repo
COPY --from=deps /repo/node_modules ./node_modules
COPY . .
RUN npm run admin:build

FROM node:22-alpine AS runtime
ENV NODE_ENV=production PORT=8080 HOSTNAME=0.0.0.0
WORKDIR /app
COPY --from=build /repo/manoksha-admin-web/.next/standalone ./
COPY --from=build /repo/manoksha-admin-web/.next/static ./manoksha-admin-web/.next/static
USER node
EXPOSE 8080
CMD ["node", "manoksha-admin-web/server.js"]
