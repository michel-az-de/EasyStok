# SCOPE.md — Escopo v1.0

**Status:** Proposed (aguardando revisão do Felipe).
**Última atualização:** 2026-05-26.

Define quais features de produto fazem parte do release v1.0 (Marco Zero). Tudo fora desta lista é v1.1+.

---

## DENTRO (12 features)

| # | Bounded Context | Bounded Context (técnico) | Justificativa |
|---|---|---|---|
| 1 | Auth / Usuário / Multi-tenant | `EasyStock.Application/UseCases/Usuario`, `Auth/*` | 25 UC, maduro, base de todo o sistema. RLS Postgres (ADR-0010) ativo. |
| 2 | Loja | `UseCases/Loja` | 10 UC, suporte para tenant. Sem loja, nada se cadastra. |
| 3 | Produto + Categoria | `UseCases/Produto`, `UseCases/Categoria` | 8 UC, catálogo essencial para vendas, storefront e estoque. |
| 4 | Estoque + Lote | `UseCases/Estoque`, `UseCases/Lote` | 8 UC, operação base. Rastreabilidade via lote. |
| 5 | Fornecedor + Compras | `UseCases/Fornecedor`, `UseCases/SugestaoCompra` | 22 UC, maduro. Reposição de estoque. |
| 6 | Cliente | `UseCases/Cliente` | 14 UC, maduro. Inclui documentos, endereços, telefones. |
| 7 | Pedido / Venda | `UseCases/Pedido` | 15 UC, hub central. Acopla com Caixa, Pagamento, Storefront. |
| 8 | Caixa **básico** | `UseCases/Caixa` (excluir Caixa V2) | 8 UC. Abrir, movimentar, fechar. ADR-0015 (Caixa V2 com state machine) é Proposed → fica para v1.1. |
| 9 | Pagamento (Efi/PIX) | `UseCases/Pagamento`, `Infra.Integrations` | Orquestrador + 1 gateway (Efi/PIX). Multi-gateway é v1.1. |
| 10 | Notificações (Email + Push) | `Infra.Notifications` | Sagas Avaliador+Coletor+Dispatcher já maduras. **Fora:** WhatsApp/SMS. |
| 11 | Storefront + Cardápio + Agendamento | `UseCases/Storefront`, `UseCases/Cardapio`, `UseCases/Agendamento` | Em construção (commits recentes em master), é o **diferencial v1.0**. |
| 12 | Financeiro **básico** | `UseCases/Financeiro` (ContasAPagar + ContasAReceber) | 31 UC totais; v1.0 cobre apenas CR/CP simples + baixa. Centros de custo e categorias avançadas = v1.1. |

---

## FORA (v1.1+ ou v2.0)

| Bounded Context | Motivo de exclusão |
|---|---|
| NFe / Fiscal | 13 UC em construção, FocusNFe instável em homologação. Risco fiscal real — não soltar sem QA dedicado. |
| Etiqueta / Rotulagem P-02 | É Etapa 5 do roadmap (ADR-0021 Accepted 2026-05-24). Pós-v1.0 por decisão explícita. |
| MAUI Mobile (`EasyStok.Mobile`) | 1.9K LoC, embrionário. App mobile não é blocker do core SaaS. |
| Analytics avançado | 46 UC mas read-only, sem dashboards prontos. Relatórios básicos no Admin cobrem v1.0. |
| Caixa V2 / Conciliação | ADR-0015 ainda **Proposed**, não Accepted. Sem decisão arquitetural fechada. |
| Admin avançado (impersonation full, tickets) | Painel atual cobre necessidades v1.0. |
| WhatsApp / SMS | TASK-EZ-WA-001 (Meta WhatsApp Cloud API) em curso mas fora do core de venda. |
| AI (OpenAI/Anthropic) | Já está `disabled` em `appsettings.json`. Não ativar. |
| Multi-gateway pagamento (fallback) | P1 do `IPagamentoGatewayRouter` é multi-gateway. v1.0 só Efi/PIX. |
| Relatórios fiscais (SPED, Sintegra) | Depende de NFe estável. v1.1+. |

---

## Governança de escopo

### Como mudar este escopo

Adicionar feature ao DENTRO durante Fases 1-5 exige:
1. PR dedicado em `docs/plan/v1.0/SCOPE.md` com motivo escrito.
2. Análise de impacto nas Fases 2-4 (vai exigir golden path novo? smoke test novo? E2E novo?).
3. Aprovação explícita do Felipe.
4. Atualização cruzada em `GOLDEN-PATHS.md` se houver novo fluxo.

Remover feature do DENTRO é mais simples: PR + nota em TECH-DEBT.md justificando o adiamento.

### O que NÃO conta como mudança de escopo

- Bug fix em feature DENTRO (é o objetivo da Fase 3).
- Adicionar campo a entity existente que destrava golden path documentado.
- Adicionar use case que serve a um golden path documentado mas falta na implementação.

### Critério de gatilho v1.1+

Após v1.0 com tag estável + 1 semana sem incidente P0, abrir `docs/plan/v1.1/SCOPE.md` priorizando débito de TECH-DEBT.md e features FORA mais demandadas.

---

## Features-borderline (decididas explicitamente)

| Item | Decisão | Motivo |
|---|---|---|
| Email confirmação OTP storefront | DENTRO | Sem isso, autenticação cliente não funciona em produção. |
| Webhook reconciliação PIX (Efi) | DENTRO | Sem isso, pagamento "fica em aberto" sem fechar o pedido. |
| Cron de expiração ClienteSession (`ExpirarClienteSessionsBackgroundService`) | DENTRO | Já implementado, é parte do storefront. |
| Health checks (`/health`) | DENTRO | Pré-requisito para Fase 2 (smoke). |
| Importação de produtos em massa (CSV) | FORA | Cadastro manual é suficiente para v1.0 com 1-N lojas-piloto. |
| Restaurante / Cardápio com fotos | DENTRO **com ressalva** | Upload de imagens cobre v1.0; CDN/otimização de imagem fica para v1.1. |
| RT (Responsável Técnico — hash SHA256, ADR-0017) | FORA | Aplica-se à Rotulagem, que é v1.1+. |
| Backup/restore automático (ADR-0012) | DENTRO | Restore-test mensal é pré-condição para v1.0 (ETK-BACKUP-001). |
