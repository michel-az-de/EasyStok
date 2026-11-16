# 1. O que levantar e providenciar antes de desenvolver

Siglas de URL (U/, G/, G-cat/, G-mer, OA, RL, HP, HC): tabela "Siglas" do [README](README.md). Detalhe nas fontes
[pedidos](fontes/api-ifood-pedidos.md) e [catálogo e loja](fontes/api-ifood-catalogo-merchant.md).

## 1.1 Checklist

| # | Item | Quem providencia | Bloqueia | Fonte |
|---|---|---|---|---|
| P01 | DECISÃO DE NEGÓCIO: reabrir a D3-02 ("iFood fora até ter conta e volume") | Felipe e a casa | Tudo | `docs/plan/erp-casa-da-baba/README.md:120`; `04-m3-atendimento.md:319-322` |
| P02 | Conta no Portal do Desenvolvedor. O cadastro cria loja de teste e app de teste | Dono do app (EasyStok) | Desenvolvimento | G/first-steps/create-account |
| P03 | Conta Profissional com CNPJ (CPF não é aceito) para homologar Order e Events | Dono do app | Homologação | U/order/homologation; U/events/polling-overview |
| P04 | App da categoria PDV (inclui Merchant, Events, Order, Catalog). A categoria "Catálogo" sozinha libera só Merchant e Catalog | Dono do app | Homologação | HP; G/first-steps/create-app |
| P05 | Tipo de app: centralizado (SaaS, várias lojas) ou distribuído (uma loja). Ver D03 | Decisão | Arquitetura | G/first-steps/create-app |
| P06 | clientId e clientSecret do app de teste e, depois, do app de produção | Portal, Meus Apps > Credenciais | Desenvolvimento | U/authentication/centralized |
| P07 | Lojas reais da casa no iFood (merchantId), com cardápio ativo, área de entrega e horário | A casa (Portal do Parceiro) | Produção | G-mer |
| P08 | Autorização da loja ao app: aba Permissões no Portal do Parceiro (centralizado) ou userCode (distribuído). Só depois da homologação | A casa | Produção | G/first-steps/request-access |
| P09 | Saída HTTPS 443 do VPS para `merchant-api.ifood.com.br`. Testar 403 da CDN do iFood para o provedor do VPS; se bloquear, abrir chamado | Infra | Desenvolvimento | G/first-steps/create-app |
| P10 | Se usar webhook: URL HTTPS pública (até 250 caracteres) cadastrada no app | Infra | Webhook | U/events/webhook-overview |
| P11 | Gerador de pedidos de teste: Portal > Testes, ou app iFood em Modo Teste com o endereço de teste e cupons de teste | Dev | Testes | G/first-steps/generate-test-order |
| P12 | Custo de uso da API `[NÃO CONFIRMADO]` | Perguntar ao iFood | Contrato | https://developer.ifood.com.br/pt-BR/support/faq |
| P13 | Termos de uso do desenvolvedor `[NÃO CONFIRMADO]`, não lidos | Jurídico ou Felipe | Contrato | https://developer.ifood.com.br/pt-BR/terms-of-use/developer |
| P14 | Quem entrega: iFood, frota própria ou ambos (muda dispatch e eventos de entregador). Ver D14 | A casa | Fluxo | U/order/workflow |
| P15 | Lista dos itens do cardápio que vão ao iFood, com o código externo (`externalCode`) de cada um | A casa e o dev | Pedidos com baixa de estoque | G-cat/introduction |
| P16 | Aprovação de tier alto (label `aprovado`): a entrega terá migração, credencial e integração externa | Felipe | Merge | `CLAUDE.md` (Tier alto); `04-m3-atendimento.md:293-300` |
| P17 | Spec da conexão por loja (`conexao_integracao`, ADR-0061 D2, spec I5.1), ainda não implementada | Dev | Vínculo empresa e merchantId | `docs/plan/integracoes/01-plug-and-play.md`; [base §2.3](fontes/easystok-base.md) |

## 1.2 Homologação: o que vai ser cobrado

| Módulo | Como | Critérios principais | Fonte |
|---|---|---|---|
| Order e Events | Automática, pelo Portal > Homologação. 5 etapas: conectividade, receber e confirmar (com ack), cancelar (com motivos), despachar (entrega própria), concluir. 1 tentativa a cada 4 h | Polling de 30 s ou webhook; ack de todos os eventos; confirmar em até 8 min; exibir bandeira, troco, cupom e quem paga, observações, código de coleta e CPF/CNPJ para NF; deduplicar eventos; sincronizar status alterado em outro sistema; tratar a Plataforma de Negociação; renovar token só quando expirar | U/order/homologation |
| Catalog | Por chamado: checklist, vídeos com data e hora, resposta em até 5 dias úteis. Testa a aplicação, não curl | Categorias, `PUT /items`, complementos com min e max, lote de preço e status, `contextModifiers`, validações, retry com backoff sem repetir 4xx, preço e status refletidos em até 2 s, 100+ itens em até 10 s | HC; HP |
| Merchant | Por chamado. Critérios próprios `[NÃO CONFIRMADO]` | | HP |
| Financial | Chamado separado. Fora do escopo | | HP |

Pré-requisito comum: desenvolvimento completo e aplicação funcional, não protótipo (HP).

## 1.3 Ambiente de teste

| Recurso | Fato | Fonte |
|---|---|---|
| Loja de teste | Criada com a conta. Só aparece aberta se houver polling ativo | G/first-steps/generate-test-order |
| Pedido de teste | Campo `isTest` no pedido | U/order/details |
| Sandbox separado de URL | Não há URL de sandbox: a base é sempre `merchant-api.ifood.com.br`; o isolamento é por app e loja de teste. **Inferência** a partir da referência única | https://developer.ifood.com.br/pt-BR/docs/references |
| EasyStok | O cofre tem `Ambiente` Sandbox e Production por credencial | `EasyStock.Domain/Integration/CredencialIntegracao.cs` |
