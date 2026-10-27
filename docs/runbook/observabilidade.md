# Observabilidade da produção (#2107)

| Camada | O que faz | Onde |
|---|---|---|
| Vigia externo (F1) | A cada 5 min, fora da VPS, testa a API, o console, o site e a loja. Se algo cair, abre uma issue `uptime` atribuída ao Felipe | `.github/workflows/uptime.yml`, `scripts/uptime/` |
| OpenObserve (F2) | Guarda por 30 dias os logs, os traces (10% das requisições da API) e as métricas da API, do Web e do Worker | stack `/opt/stacks/observabilidade` |
| Collector OTLP (F2) | Recebe OTLP sem senha na rede `edge`, em `ez-otel:4317`, e repassa ao OpenObserve com autenticação | mesma stack |
| Healthcheck do Worker (F2) | O Worker regrava `/tmp/ez-worker-vivo` a cada 30 s enquanto os loops do motor batem heartbeat. Arquivo com mais de 120 s marca o container como `unhealthy` | override `compose.observabilidade.yaml` |

## Operar

```bash
scripts/deploy/observabilidade-deploy.sh subir      # primeira vez ou atualização da stack
scripts/deploy/observabilidade-deploy.sh ligar      # EasyStok passa a enviar; volta atrás se não ficar saudável
scripts/deploy/observabilidade-deploy.sh status     # containers e quanto chegou na última hora
scripts/deploy/observabilidade-deploy.sh acesso     # túnel e onde está a senha
scripts/deploy/observabilidade-deploy.sh desligar   # EasyStok para de enviar
```

`ligar` acrescenta `compose.observabilidade.yaml` ao `COMPOSE_FILE` do `.env` do EasyStok. Daí em diante o `vps-deploy.sh` já sobe com ele.

## Consultar

Abra a tela pelo túnel (`acesso`). Vá em Logs e escolha o stream `easystok`. Para filtrar por serviço, use `service_name`. A partir do `trace_id` de um log, o trace correspondente abre em Traces.

## Limites

- Nível dos logs que chegam: API a partir de Warning (`appsettings.Production.json`); Web e Worker a partir de Information, porque não têm configuração própria de produção. Acompanhe o disco no `status` nas primeiras semanas: o OpenObserve divide a VPS com o Postgres.
- O healthcheck do Worker só fica `healthy` depois do primeiro deploy com o `ArquivoDeVidaService`. Antes disso aparece `unhealthy`, sem efeito: o `ligar` e o `vps-deploy.sh` olham se o Worker está rodando e sem reinício.
- Ainda não há alerta no OpenObserve. Queda é avisada pelo vigia externo.
- O redis fica fora do override, porque recriá-lo apaga as sessões.
