# Casa da Baba Mobile

PWA (Progressive Web App) + backend .NET pra integrar com o EasyStock. Ferramenta operacional de cozinha pra gestão de produção, pedidos, estoque e caixa do Casa da Baba.

## O que tem nessa pasta

```
casa-da-baba-mobile/
├── CLAUDE.md              ← instruções passo-a-passo pro Claude Code integrar
├── README.md              ← este arquivo
├── INSTALL.md             ← passo a passo manual (caso não use Claude Code)
├── API-CONTRACT.md        ← especificação dos endpoints REST
├── pwa/                   ← o app em si (HTML/CSS/JS vanilla)
│   ├── index.html
│   ├── manifest.json
│   ├── sw.js              ← service worker (cache offline)
│   ├── sync.js            ← fila de sincronização com backend
│   └── icons/
│       ├── icon-192.png
│       ├── icon-512.png
│       └── icon-maskable-512.png
└── backend/               ← C# drop-in pra EasyStock
    ├── Models/            ← entidades EF Core (7 no total)
    ├── DTOs/              ← data transfer objects
    ├── Controllers/       ← SyncController
    ├── Mobile/            ← MobileModule (DI + static files + CORS)
    └── Migrations/
        └── 001_CreateMobileSchema.sql
```

## Uso rápido (com Claude Code)

1. Coloque essa pasta ao lado da solução do EasyStock
2. Abra o Claude Code na pasta raiz (que contém o `.sln` do EasyStock + essa pasta)
3. Diga: *"Leia o CLAUDE.md da pasta casa-da-baba-mobile e integre ao EasyStock"*
4. Claude Code vai fazer os 9 passos do CLAUDE.md

## Uso manual

Se preferir fazer você mesmo, siga o `INSTALL.md`.

## Arquitetura

- **Frontend**: PWA rodando no celular. Offline-first (tudo persiste em localStorage). Ao ficar online, sincroniza com backend via fila.
- **Backend**: 1 controller (`SyncController`) com 2 endpoints (push e pull). Regras de estoque replicadas. Reconciliação last-write-wins por timestamp.
- **Banco**: 7 tabelas novas, prefixadas com `mobile_` pra não colidir com o schema do EasyStock.
- **Sincronização**: não é real-time. Cliente envia quando muda algo, puxa a cada 30s ou ao voltar online. Suficiente pro caso (2 devices, cozinha).

## Stack

- Frontend: HTML + CSS + vanilla JS, sem build step, sem framework
- Backend: .NET 9, EF Core, PostgreSQL (Azure)
- Deployment: o próprio EasyStock serve o PWA em `/pwa/`

## Como o Felipe usa

1. Abre `http://ip-do-computador:5000/pwa/` no celular
2. Android (Chrome): menu 3 pontos → "Adicionar à tela inicial"
3. iOS (Safari): botão compartilhar → "Adicionar à Tela de Início"
4. Aparece ícone "CB" na home do celular. Toca, abre fullscreen como app nativo.
5. Usa normalmente: registra produção, pedidos, caixa. Sincroniza sozinho com o EasyStock em background.

## Limitações conhecidas

- Sem autenticação (rede local, uso pessoal)
- Sem push notifications (não precisa)
- Fotos em base64 no banco (pra simplicidade; se crescer, migrar pra Blob Storage)
- Service worker só funciona em HTTPS ou localhost. Em IP local HTTP, o app roda mas sem cache offline

## Para crescer pra ERP

Quando o EasyStock virar ERP de verdade, esse módulo mobile alimenta:
- Relatório de produção por dia/semana/mês
- Histórico de pedidos por cliente
- Fluxo de caixa consolidado
- Lotes com fotos pra rastreabilidade
- Catálogo de produtos com revisão humana (campo `is_custom` sinaliza o que veio do app e precisa revisão)

Todas as tabelas já têm `created_at`, `updated_at` e `last_device_id` pra auditoria.
