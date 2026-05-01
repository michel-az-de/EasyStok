# GCP Deploy — EasyStock

> Plano de migração Azure (desabilitado) → GCP. Pivot principal pra reduzir custo a ~$0 nos primeiros 90 dias.

## Por quê GCP
- $300 free credit por 90 dias
- Cloud Run free tier permanente: 2M requests/mês, 360k GB-s, 180k vCPU-s
- Cloud SQL com `db-f1-micro` ~$7/mês (vs PG Flexible $50+/mês na Azure)

## Pré-requisitos
1. Conta GCP criada (`https://console.cloud.google.com`)
2. Billing ativo (cartão obrigatório mas não cobra durante credit)
3. Project ID definido (ex: `easystock-prod`)
4. `gcloud` CLI instalado e autenticado: `gcloud auth login`

## Estrutura do deploy

```
GCP Project (easystock-prod)
├── Cloud Run service: easystock-api    (porta 8080, 512MB, min=0, max=3)
├── Cloud Run service: easystock-web    (porta 8080, 512MB, min=0, max=3)
├── Cloud Run service: easystock-admin  (porta 8080, 256MB, min=0, max=2)
├── Cloud SQL instance: pg-easystock    (db-f1-micro, PG 17, public IP + connector)
├── Secret Manager: connection strings, JWT key, Efí keys, SMTP
└── Artifact Registry: us-central1-docker.pkg.dev/easystock-prod/images
```

## Passos

### 1. Bootstrap
```bash
export PROJECT_ID=easystock-prod
gcloud config set project $PROJECT_ID
gcloud services enable run.googleapis.com sqladmin.googleapis.com \
  artifactregistry.googleapis.com secretmanager.googleapis.com \
  cloudbuild.googleapis.com
```

### 2. Cloud SQL
```bash
gcloud sql instances create pg-easystock \
  --database-version=POSTGRES_17 \
  --tier=db-f1-micro \
  --region=us-central1 \
  --root-password='SENHA-FORTE-AQUI'

gcloud sql databases create easystock --instance=pg-easystock
```

### 3. Secrets
```bash
echo -n "Host=...;Database=easystock;Username=postgres;Password=..." | \
  gcloud secrets create db-connection --data-file=-
echo -n "JWT-SIGNING-KEY-32CHARS+" | gcloud secrets create jwt-key --data-file=-
# repetir pra Efi:ClientId, Efi:ClientSecret, Efi:WebhookSecret, SMTP
```

### 4. Build & push images
```bash
gcloud artifacts repositories create images \
  --repository-format=docker --location=us-central1

gcloud builds submit --tag us-central1-docker.pkg.dev/$PROJECT_ID/images/api:latest \
  -f Dockerfile.cloudrun.api .
gcloud builds submit --tag us-central1-docker.pkg.dev/$PROJECT_ID/images/web:latest \
  -f Dockerfile.cloudrun.web .
gcloud builds submit --tag us-central1-docker.pkg.dev/$PROJECT_ID/images/admin:latest \
  -f Dockerfile.cloudrun.admin .
```

### 5. Deploy services
```bash
gcloud run deploy easystock-api \
  --image us-central1-docker.pkg.dev/$PROJECT_ID/images/api:latest \
  --region us-central1 --platform managed --allow-unauthenticated \
  --add-cloudsql-instances $PROJECT_ID:us-central1:pg-easystock \
  --set-secrets "ConnectionStrings__Default=db-connection:latest,Jwt__Key=jwt-key:latest" \
  --memory 512Mi --max-instances 3 --min-instances 0
# repetir pra web e admin (sem Cloud SQL connector — vão via API)
```

### 6. Migrations
```bash
# rodar localmente apontando pra Cloud SQL via proxy:
cloud-sql-proxy $PROJECT_ID:us-central1:pg-easystock &
ConnectionStrings__Default="Host=127.0.0.1;..." \
  dotnet ef database update \
  --project EasyStock.Infra.Postgre \
  --startup-project EasyStock.Api
```

### 7. Webhook Efí
- Apontar `https://easystock-api-xxx.run.app/api/webhooks/pix` no painel Efí
- Configurar `Efi:WebhookSecret` no Secret Manager

## Script
`scripts/gcp-deploy.sh` automatiza passos 4-5. Rodar após bootstrap manual (1-3) e migrations (6).

## Monitoramento de custo
- `gcloud billing budgets create` com alerta em $50
- Cloud Run `min-instances=0` = $0 quando ocioso
- Cloud SQL é o caro (~$7/mês mesmo ocioso) — pausar com `gcloud sql instances patch pg-easystock --activation-policy=NEVER` se ficar dias sem uso

## Rollback
- Cloud Run mantém revisões. `gcloud run services update-traffic easystock-api --to-revisions=PREV=100`
