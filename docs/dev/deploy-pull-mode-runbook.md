# Runbook — virar o deploy da VM para modo pull (issue #605, fatia 4)

Pré-requisito de infra montado nas fatias 1-3 (commits `43d951d8`, `08eb1d63`,
`b2bbc756`): o workflow `build-images.yml` publica `ghcr.io/michel-az-de/easystok-{api,web,admin}`
com tags `:latest` e `:<sha>` a cada push em master; `docker-compose.azure.images.yml`
troca o `build:` pelo `image:` do GHCR; `vm-deploy.sh` aceita `EASYSTOK_DEPLOY_MODE=pull`.

Falta só o passo operacional na VM — **requer um PAT** e por isso não foi automatizado.

## 1. Gerar o PAT (uma vez)

GitHub → Settings → Developer settings → Personal access tokens → **Tokens (classic)**
→ Generate new token (classic). Marcar **somente** o escopo `read:packages`. Sem
expiração curta (ou anotar para renovar). Copiar o valor.

## 2. Gravar o token no .env da VM (sem passar pelo shell history)

Na VM (`easystok-vm`, dir `/home/azureuser/easystok`), acrescentar ao `.env`:

    GHCR_USER=michel-az-de
    GHCR_TOKEN=<pat read:packages>

Preferir `az vm run-command invoke` com o conteúdo, ou editar direto por SSH — não
ecoar o token em comando que fique no histórico. As vars já estão documentadas em
`.env.azure.example`.

## 3. Teste manual (ainda sem tocar o cron)

    cd /home/azureuser/easystok
    EASYSTOK_DEPLOY_MODE=pull bash scripts/docker/vm-deploy.sh --force

Esperado: `docker login ghcr.io` ok → `pull das imagens :<sha>` → snapshot pre-deploy
→ `up -d` sem build → linha final `container_web GIT_SHA` == HEAD. Se a imagem do sha
ainda não existe (CI em andamento), o script aborta com exit 4 sem avançar nada — é o
comportamento correto (o CI é o gate do deploy).

Verificação independente (receita `vm-deploy-verify-recipe`):

    docker inspect easystok-web --format '{{ range .Config.Env }}{{ println . }}{{ end }}' | grep GIT_SHA
    # e/ou o endpoint /health.commit == HEAD

## 4. Virar o cron

Trocar a linha do crontab do `azureuser` para:

    */5 * * * * EASYSTOK_DEPLOY_MODE=pull bash /home/azureuser/easystok/scripts/docker/vm-deploy.sh

O modo build continua disponível como fallback: basta remover o `EASYSTOK_DEPLOY_MODE=pull`
(ou setar `=build`) que a VM volta a buildar localmente.

## 5. Medir o ganho (fechar a issue com número)

Cronometrar um push→VM em modo pull (esperado: segundos-a-poucos-minutos, contra a
janela de ~2h do build na VM, issue #751). Registrar antes/depois no fechamento da #605.

## Rollback

- Reverter o cron para build-mode (passo 4 ao contrário) — imediato.
- As imagens antigas seguem no GHCR por tag `:<sha>`; `EASYSTOK_IMAGE_TAG=<sha antigo>
  docker compose -f docker-compose.azure.yml -f docker-compose.azure.images.yml up -d`
  volta a VM a uma versão específica sem rebuild.
