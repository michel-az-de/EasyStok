#!/usr/bin/env bash
# Deploy completo do EasyStock no Google Cloud Run + Cloud SQL Postgres.
# Uso:
#   PROJECT_ID=easystok-XXXXX ./scripts/gcp-deploy.sh
#
# Pré-requisito: gcloud autenticado e com permissão no PROJECT_ID.

set -euo pipefail

PROJECT_ID="${PROJECT_ID:?Defina PROJECT_ID, ex: PROJECT_ID=easystok-12345}"
REGION="${REGION:-southamerica-east1}"
SQL_INSTANCE="${SQL_INSTANCE:-easystock-pg}"
SQL_DB="${SQL_DB:-EasyStockDb}"
SQL_USER="${SQL_USER:-easystock}"
SQL_PASS="${SQL_PASS:-$(openssl rand -base64 24 | tr -d '/+=' | head -c 20)Aa1!}"
JWT_SECRET="${JWT_SECRET:-$(openssl rand -base64 48 | tr -d '/+=' | head -c 48)}"

echo "==> Projeto: $PROJECT_ID  Região: $REGION"

gcloud config set project "$PROJECT_ID"

echo "==> Habilitando APIs necessárias"
gcloud services enable \
  run.googleapis.com \
  sqladmin.googleapis.com \
  cloudbuild.googleapis.com \
  artifactregistry.googleapis.com \
  secretmanager.googleapis.com

echo "==> Criando Cloud SQL Postgres (db-f1-micro, ~\$10/mês)"
if ! gcloud sql instances describe "$SQL_INSTANCE" --quiet 2>/dev/null; then
  gcloud sql instances create "$SQL_INSTANCE" \
    --database-version=POSTGRES_15 \
    --tier=db-f1-micro \
    --region="$REGION" \
    --storage-size=10GB \
    --storage-type=HDD \
    --backup \
    --availability-type=zonal \
    --no-deletion-protection
fi

if ! gcloud sql databases describe "$SQL_DB" --instance="$SQL_INSTANCE" --quiet 2>/dev/null; then
  gcloud sql databases create "$SQL_DB" --instance="$SQL_INSTANCE"
fi

if ! gcloud sql users list --instance="$SQL_INSTANCE" --format="value(name)" | grep -q "^$SQL_USER$"; then
  gcloud sql users create "$SQL_USER" --instance="$SQL_INSTANCE" --password="$SQL_PASS"
fi

CONN_NAME=$(gcloud sql instances describe "$SQL_INSTANCE" --format='value(connectionName)')
echo "==> Cloud SQL pronto. Connection name: $CONN_NAME"

CONN_STR="Host=/cloudsql/$CONN_NAME;Database=$SQL_DB;Username=$SQL_USER;Password=$SQL_PASS;SSL Mode=Disable"

echo "==> Salvando secrets no Secret Manager"
echo -n "$CONN_STR"   | gcloud secrets create easystock-db   --data-file=- 2>/dev/null \
  || echo -n "$CONN_STR"   | gcloud secrets versions add easystock-db   --data-file=-
echo -n "$JWT_SECRET" | gcloud secrets create easystock-jwt  --data-file=- 2>/dev/null \
  || echo -n "$JWT_SECRET" | gcloud secrets versions add easystock-jwt  --data-file=-

echo "==> Build + deploy API"
gcloud builds submit --tag "gcr.io/$PROJECT_ID/easystock-api" \
  --file Dockerfile.cloudrun.api .
gcloud run deploy easystock-api \
  --image "gcr.io/$PROJECT_ID/easystock-api" \
  --region "$REGION" \
  --platform managed \
  --allow-unauthenticated \
  --add-cloudsql-instances "$CONN_NAME" \
  --memory 512Mi --cpu 1 --min-instances 0 --max-instances 2 \
  --set-env-vars "ASPNETCORE_ENVIRONMENT=Production,Database__Provider=PostgreSql,Jwt__Issuer=EasyStock,Jwt__Audience=EasyStock,Cors__AllowedOrigins__0=*" \
  --set-secrets "ConnectionStrings__DefaultConnection=easystock-db:latest,Jwt__SecretKey=easystock-jwt:latest"

API_URL=$(gcloud run services describe easystock-api --region "$REGION" --format='value(status.url)')
echo "==> API: $API_URL"

echo "==> Build + deploy Web"
gcloud builds submit --tag "gcr.io/$PROJECT_ID/easystock-web" \
  --file Dockerfile.cloudrun.web .
gcloud run deploy easystock-web \
  --image "gcr.io/$PROJECT_ID/easystock-web" \
  --region "$REGION" \
  --platform managed \
  --allow-unauthenticated \
  --memory 512Mi --cpu 1 --min-instances 0 --max-instances 2 \
  --set-env-vars "ASPNETCORE_ENVIRONMENT=Production,ApiSettings__BaseUrl=$API_URL/api"

WEB_URL=$(gcloud run services describe easystock-web --region "$REGION" --format='value(status.url)')
echo "==> Web: $WEB_URL"

echo "==> Build + deploy Admin"
gcloud builds submit --tag "gcr.io/$PROJECT_ID/easystock-admin" \
  --file Dockerfile.cloudrun.admin .
gcloud run deploy easystock-admin \
  --image "gcr.io/$PROJECT_ID/easystock-admin" \
  --region "$REGION" \
  --platform managed \
  --allow-unauthenticated \
  --memory 512Mi --cpu 1 --min-instances 0 --max-instances 1 \
  --set-env-vars "ASPNETCORE_ENVIRONMENT=Production,ApiBaseUrl=$API_URL/api,EasyStockWebUrl=$WEB_URL"

ADMIN_URL=$(gcloud run services describe easystock-admin --region "$REGION" --format='value(status.url)')

echo
echo "===================================="
echo " EasyStock no Cloud Run — pronto"
echo "===================================="
echo "API   : $API_URL"
echo "Web   : $WEB_URL"
echo "Admin : $ADMIN_URL"
echo
echo "Senha do PG (guarde em local seguro): $SQL_PASS"
echo "Connection name: $CONN_NAME"
