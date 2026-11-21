# baba-diag remoto (ADR-0069). Contexto: tools/mcp-baba. Build e subida: scripts/deploy/mcp-deploy.sh subir.
FROM node:24-alpine@sha256:116b800c3754b440ee0aa3ed2f20f5bf3d99a554bfd9adeea09ea8017f6e85f5
RUN apk add --no-cache openssh-client
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --omit=dev --no-audit --no-fund && npm cache clean --force
COPY src ./src
ARG GIT_SHA=desconhecido
ENV GIT_SHA=$GIT_SHA NODE_ENV=production
# uid 1000 (node): dono da chave SSH e do arquivo de tokens montados pela stack
USER node
EXPOSE 8080
CMD ["node", "src/diag/http.mjs"]
