# Go-live do atendimento: roteiro de publicação e validação (F13)

Issue: #1243 · Medido em 2026-10-01 · Base: [11-console-fechamento.md](11-console-fechamento.md)

## Onde está cada coisa (medido)

| Peça | Hoje em produção | No master |
|---|---|---|
| API (`api.easystok.online`, VPS `/opt/stacks/easystok`) | `buildSha 170e6117` (30/09) | F06, F07, F18 e correções de 01/10 |
| Console (`app.easystok.online`) | HTML estático de 30/09 22:33, servido pelo Caddy, que repassa `/api` para a API | F06, F07, F18 (e F16 quando a #1304 entrar) |

O console publicado é o modo API (`VITE_FONTE_DADOS=api`) na mesma origem da API: `VITE_API_BASE` fica
vazio porque o Caddy de `app.easystok.online` já repassa `/api/*` (medido: `/api/atendimento/conversas` → 401).

## Ordem

```
1 aprovar e mergear F08 (#1266) e F16 (#1304)          ← Felipe aplica a label `aprovado`
2 KEK no .env e no compose da VPS                        ← Felipe (sem ela a API de produção não sobe)
3 deploy da API pelo script                              ← Felipe
4 publicar o console do mesmo SHA                        ← Felipe
5 conferir versão e smoke                                ← agente pode conferir de fora
6 validações com dinheiro, canal e impressora reais      ← Felipe
```

### 2. Chave mestra (KEK) — antes do deploy da F16

No `.env` de `/opt/stacks/easystok` (chmod 600), um valor Base64 de 32 bytes gerado na própria VPS
(`openssl rand -base64 32`; nunca pelo chat):

```
EZ_CRYPTO_KEK_ID=kek-2026-10
EZ_CRYPTO_KEK=<saída do openssl>
```

E no serviço da API em `/opt/stacks/easystok/compose.yaml` (o `vps-deploy.sh` usa o compose da VPS,
não o do repositório):

```yaml
      EZ_CRYPTO_KEK_ID: "${EZ_CRYPTO_KEK_ID:?defina a KEK}"
      EZ_CRYPTO_KEK: "${EZ_CRYPTO_KEK:?defina a KEK}"
```

Rotação: a atual vai para `EZ_CRYPTO_KEK_ANTERIOR_ID`/`EZ_CRYPTO_KEK_ANTERIOR`, a nova entra no lugar.

### 3. API

```bash
scripts/deploy/vps-deploy.sh --dry-run
scripts/deploy/vps-deploy.sh
```

O script trava contra deploy simultâneo, faz `pg_dump` em `~/backups`, sobe, espera healthy, confere o
`GIT_SHA` e volta sozinho se falhar. Migrations novas desde 30/09 rodam no startup: F08 (motivo da
escalada; RLS em `ocorrencias`; índice de uma viagem ativa por pedido) e F16 (colunas e RLS em
`credencial_integracao`).

**Risco conhecido:** o índice `ux_viagem_paradas_pedido_ativo` falha se já houver o mesmo pedido em duas
paradas não entregues. O script volta a imagem anterior; antes do deploy dá para conferir:

```sql
select "PedidoId", count(*) from viagem_paradas where "EntregueEm" is null group by 1 having count(*) > 1;
```

### 4. Console

Do mesmo SHA publicado na API:

```bash
cd EasyStock.Console && npm ci && VITE_FONTE_DADOS=api npm run build
```

Copiar `dist/index.html` para a pasta que o Caddy serve em `app.easystok.online`, guardando antes a
versão atual como `index.html.prev` (rollback = copiar de volta). Alternativa em contêiner:
`Dockerfile.web` com `--build-arg VITE_FONTE_DADOS=api` e `API_UPSTREAM=http://ez-api:8080`
(o nginx já repassa o SSE sem buffer; validado em 01/10).

### 5. Conferência

- `GET https://api.easystok.online/health/version` → `buildSha` igual ao SHA publicado.
- `https://app.easystok.online` abre o login; depois do login a faixa do rodapé diz "Conversas ao vivo do EasyStok".
- Gestão › Entregas e integrações mostra os cartões (só Admin) e o botão Testar responde.

## 6. Validações que só o Felipe fecha

| # | Fatia | Validação |
|---|---|---|
| 1 | F16 | Cadastrar a chave real do Mercado Pago e do Google Maps na aba Integrações; Testar dá "Ok". |
| 2 | F03 | Pedido pela conversa com Pix ou cartão: o link chega ao cliente e vira pago (sandbox do MP primeiro). |
| 3 | F18 | Conversa do chat do site: nome pelo lápis, telefone, endereço, Salvar cadastro, Gerar cobrança. |
| 4 | F05 | Pedido pago aparece na Cozinha no dia de produção e muda de passo pela tela. |
| 5 | F04 | Viagem com entregador sai e os pedidos vão para "saiu para entrega". |
| 6 | F06 | Foto enviada pelo console chega ao WhatsApp. |
| 7 | — | Mensagem real no WhatsApp da loja aparece no console e a resposta chega no celular. |

Pré-requisitos externos da onda 0 que essas validações exigem: credenciais `EZ_META_*` no `.env`,
webhook da Meta configurado, credenciais do Mercado Pago (teste e produção) e templates aprovados.

## Fatos de 01/10 que este roteiro assume

- Itens de código da F13 corrigidos no PR desta issue: `Dockerfile.web` gera o modo API; nginx com
  destino configurável e SSE sem buffer; operador comum sem aviso falso de "sem permissão" ao abrir
  o console (expediente e avisos da Ficha são de Admin); `10-console.md` cita as variáveis certas.
- A matriz do `10-console.md` já estava corrigida (S24, S25, S42, S43 e S46 como "Sim").
