# Compose de produção (espelho)

Espelho auditável do stack `/opt/stacks/easystok` da VPS (#2599, item da #2372). O deploy
(`scripts/deploy/vps-deploy.sh`) **continua usando o compose da VPS**; estes arquivos não são enviados.

## Mapa do `COMPOSE_FILE`

O `.env` da VPS define
`COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.wppconnect.yaml:compose.observabilidade.yaml`.

| Na VPS | No repo |
|---|---|
| `compose.yaml` | `scripts/deploy/producao/compose.yaml` |
| `compose.override.yaml` | `scripts/deploy/producao/compose.override.yaml` |
| `compose.wppconnect.yaml` | `scripts/deploy/wppconnect/compose.yaml` |
| `compose.observabilidade.yaml` | `scripts/deploy/observabilidade/compose.easystok.yaml` |

Os dois últimos já eram a origem dos arquivos da VPS e conferiam em 14/11/2026.

## `__NA_VPS__`

Todo valor de chave sensível (KEY, SECRET, TOKEN, PASSWORD, KEK, CONNECTION, AUTH, WEBHOOK...) foi
trocado por `__NA_VPS__`. Pode ser uma referência `${VAR}` ao `.env` ou um literal: o espelho não
distingue. Por isso estes arquivos não sobem um stack.

## Atualizar o espelho

Da máquina de dev (PowerShell), sem trazer segredo:

```powershell
$script = @'
cd /opt/stacks/easystok || exit 1
for f in compose.yaml compose.override.yaml; do
  echo "### $f"
  sed -E -e 's/^([[:space:]-]*[A-Za-z0-9_]*(KEY|SECRET|TOKEN|PASSWORD|PASS|PWD|KEK|CONNECTION|DSN|AUTH|CREDENTIAL|WEBHOOK)[A-Za-z0-9_]*[[:space:]]*[:=][[:space:]]*)(.+)$/\1__NA_VPS__/I' -e 's/(Password|Pwd)=[^;"]*/\1=__NA_VPS__/Ig' "$f"
done
'@
$script.Replace("`r","") | ssh hostinger "bash -s"
```

Cole cada bloco no arquivo correspondente e revise o diff antes do commit; o gitleaks do CI é a
segunda barreira.
