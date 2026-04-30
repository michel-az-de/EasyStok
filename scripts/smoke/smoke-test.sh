#!/bin/bash
# EasyStok Smoke Test Script
# Uso: ./scripts/smoke/smoke-test.sh [BASE_URL]
# Default: http://localhost:8080
#
# Variáveis de ambiente necessárias:
#   SMOKE_EMAIL    — e-mail de uma conta ativa (ex: export SMOKE_EMAIL=admin@empresa.com)
#   SMOKE_PASSWORD — senha da conta         (ex: export SMOKE_PASSWORD=SenhaSegura123)
#
# NUNCA commitar credenciais reais neste script.

set -euo pipefail

BASE="${1:-http://localhost:8080}"

if [ -z "${SMOKE_EMAIL:-}" ] || [ -z "${SMOKE_PASSWORD:-}" ]; then
    echo "❌ Variáveis SMOKE_EMAIL e SMOKE_PASSWORD não definidas."
    echo "   export SMOKE_EMAIL=seu@email.com"
    echo "   export SMOKE_PASSWORD=SuaSenha"
    exit 1
fi
PASS=0
FAIL=0
TOTAL=0

green() { echo -e "\033[32m✅ $1\033[0m"; }
red() { echo -e "\033[31m❌ $1\033[0m"; }
yellow() { echo -e "\033[33m⚠️  $1\033[0m"; }

check() {
    local name="$1"
    local url="$2"
    local expected="${3:-200}"
    TOTAL=$((TOTAL + 1))

    status=$(curl -s -o /dev/null -w "%{http_code}" --max-time 10 "$url" 2>/dev/null || echo "000")

    if [ "$status" = "$expected" ]; then
        green "$name — HTTP $status"
        PASS=$((PASS + 1))
    else
        red "$name — HTTP $status (esperado $expected)"
        FAIL=$((FAIL + 1))
    fi
}

check_auth() {
    local name="$1"
    local url="$2"
    local token="$3"
    local expected="${4:-200}"
    TOTAL=$((TOTAL + 1))

    status=$(curl -s -o /dev/null -w "%{http_code}" --max-time 10 \
        -H "Authorization: Bearer $token" "$url" 2>/dev/null || echo "000")

    if [ "$status" = "$expected" ]; then
        green "$name — HTTP $status"
        PASS=$((PASS + 1))
    else
        red "$name — HTTP $status (esperado $expected)"
        FAIL=$((FAIL + 1))
    fi
}

echo "=========================================="
echo "  EasyStok Smoke Test"
echo "  Base URL: $BASE"
echo "  Data: $(date +%Y-%m-%d\ %H:%M:%S)"
echo "=========================================="
echo ""

# --- INFRA ---
echo "--- INFRAESTRUTURA ---"
check "Health Check" "$BASE/health"
check "Swagger UI" "$BASE/swagger/ui/index.html"

# --- AUTH ---
echo ""
echo "--- AUTENTICAÇÃO ---"

# Login
LOGIN_RESPONSE=$(curl -s --max-time 10 -X POST "$BASE/api/auth/login" \
    -H "Content-Type: application/json" \
    -d "{\"email\":\"${SMOKE_EMAIL}\",\"senha\":\"${SMOKE_PASSWORD}\"}" 2>/dev/null || echo '{}')

TOKEN=$(echo "$LOGIN_RESPONSE" | grep -o '"token":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -n "$TOKEN" ] && [ "$TOKEN" != "null" ]; then
    green "Login Admin — Token obtido"
    PASS=$((PASS + 1))
else
    red "Login Admin — Falha ao obter token"
    FAIL=$((FAIL + 1))
    yellow "Verifique se o seed foi executado e as credenciais estão corretas"
    TOKEN=""
fi
TOTAL=$((TOTAL + 1))

# --- ENDPOINTS AUTENTICADOS ---
if [ -n "$TOKEN" ]; then
    echo ""
    echo "--- ENDPOINTS CORE ---"
    check_auth "GET /api/auth/me" "$BASE/api/auth/me" "$TOKEN"
    check_auth "GET /api/produtos" "$BASE/api/produtos" "$TOKEN"
    check_auth "GET /api/categorias" "$BASE/api/categorias" "$TOKEN"
    check_auth "GET /api/lojas" "$BASE/api/lojas" "$TOKEN"
    check_auth "GET /api/fornecedores" "$BASE/api/fornecedores" "$TOKEN"
    check_auth "GET /api/estoque" "$BASE/api/estoque" "$TOKEN"
    check_auth "GET /api/usuarios" "$BASE/api/usuarios" "$TOKEN"
    check_auth "GET /api/notificacoes" "$BASE/api/notificacoes" "$TOKEN"
    check_auth "GET /api/planos" "$BASE/api/planos" "$TOKEN" # planos pode ser público
    check_auth "GET /api/configuracoes" "$BASE/api/configuracoes" "$TOKEN"

    echo ""
    echo "--- ANALYTICS & INTELIGÊNCIA ---"
    check_auth "GET /api/analytics/dashboard" "$BASE/api/analytics/dashboard" "$TOKEN"
    check_auth "GET /api/inteligencia/estoque-baixo" "$BASE/api/inteligencia/estoque-baixo" "$TOKEN"
    check_auth "GET /api/inteligencia/proximo-vencimento" "$BASE/api/inteligencia/proximo-vencimento" "$TOKEN"
    check_auth "GET /api/inteligencia/board" "$BASE/api/inteligencia/board" "$TOKEN"

    echo ""
    echo "--- MOVIMENTAÇÕES ---"
    check_auth "GET /api/movimentacoes" "$BASE/api/movimentacoes" "$TOKEN"
else
    yellow "Pulando testes autenticados — sem token"
fi

# --- RESUMO ---
echo ""
echo "=========================================="
echo "  RESULTADO"
echo "  Total: $TOTAL | Pass: $PASS | Fail: $FAIL"
if [ "$FAIL" -eq 0 ]; then
    green "TODOS OS TESTES PASSARAM"
else
    red "$FAIL TESTE(S) FALHARAM"
fi
echo "=========================================="

exit $FAIL
