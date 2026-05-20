# Sessao Compras Inteligente — Fase 1

Data: 2026-05-20 12:18
Worktree: .claude/worktrees/thirsty-einstein-4bb540 (branch dev/thirsty-einstein-4bb540)
Identidade Git: felipe.azevedo@gmail.com / michel-az-de
Status final: completo (Fase 1), pendente validacao manual em navegador

## Contexto

Diagnostico do modulo de compras: existiam "dois modulos que nao se falam".
A lista de compras (web) era um bloco de notas manual (texto livre, sem vinculo
com produto/estoque/fornecedor), enquanto a inteligencia de compras
(estoque baixo, velocidade de saida, sugestao de reposicao, fornecedor preferido)
ja existia no backend mas estava plugada so na Calculadora de Producao do PWA.

Decisao do Felipe (visao de produto + roadmap aprovado): transformar a lista em
"assistente de reposicao". Escopo Fase 1 = Web, capacidades gerar automatica +
imprimir + WhatsApp. Fornecedor adiado para Fase 2.

Plano: C:/Users/f.michel.de.azevedo/.claude/plans/debug-ux-copy-preciso-spicy-sprout.md

## O que foi feito

- **Gerar automatica**: novo use case `GerarListaComprasUseCase` + endpoint
  `POST /api/listas-compras/gerar` (cria lista + itens em 1 transacao via cascata EF).
  Tela web `Gerar.cshtml` consome `inteligencia/sugestao-reposicao` (reuso de
  `ObterSugestaoReposicaoUseCase`), pre-seleciona itens abaixo do minimo com
  quantidade sugerida + custo estimado, total dinamico (Alpine), marcar/desmarcar.
  Botao "Gerar do estoque baixo" na Index + empty state reescrito.
- **Imprimir**: `Imprimir.cshtml` (Layout=null) + `wwwroot/css/lista-print.css`,
  agrupado por categoria, com caixas de conferencia. Botao no Detail.
- **WhatsApp**: botao no Detail monta `wa.me/?text=` server-side com a lista
  formatada (empresa, data, itens com quantidade).

## O que ficou pendente

- Validacao manual em navegador (nao executada): exige Web+API+Postgres com
  empresa/loja e produtos abaixo do minimo. Recomendado antes do merge.
- Confirmar em runtime: cascata insert lista+itens (EF), model-binding do form
  indexado (Itens[i].*), precedencia de rota /gerar vs /{id}.
- Fase 2 (roadmap): sugestao de fornecedor (onde comprar) — backend ja tem
  `PreviewSugestaoCompraUseCase` + dados de Fornecedor. Requer FornecedorId no
  DTO de estoque baixo.

## Decisoes tomadas

- Fonte da geracao = endpoint `sugestao-reposicao` (ja traz NomeProduto +
  QuantidadeSugerida + CustoEstimado), em vez de `estoque-baixo` (sem nome/qtd).
- Insercao em lote por cascata EF (lista.Itens), sem alterar IListaComprasRepository.
- Quantidade sugerida = a do backend (nao recalculei minimo-atual).
- Print via window.print() (padrao do projeto, ver Pedidos/Recibo.cshtml), nao QuestPDF.
- CSS de impressao em arquivo externo (nao inline) por causa do teste de
  higiene de design system (RazorViewHygieneTests bloqueia hex em .cshtml).

## Commits criados

- ce0f3689: feat(web): lista de compras inteligente — gerar do estoque baixo, imprimir e WhatsApp

## Branches criadas/deletadas

- Trabalho na branch existente dev/thirsty-einstein-4bb540 (worktree). PR para master.

## Validacao

- dotnet build EasyStok.sln: 0 erros (31 warnings pre-existentes).
- dotnet build EasyStock.Web: 0 erros (views Razor compilam).
- dotnet test ArchitectureTests: 16/16. Husky pre-commit (rotulagem-architecture-tests): ok.

## Proxima acao recomendada

- Subir Web+API local com produto abaixo do minimo e validar o fluxo end-to-end
  (gerar -> revisar -> criar -> imprimir -> WhatsApp).
- Apos validacao, mergear o PR.

## Referencias

- Plano: ~/.claude/plans/debug-ux-copy-preciso-spicy-sprout.md
- Reuso backend: EasyStock.Application/UseCases/Inteligencia/SugestaoReposicao/
- Modelo de impressao: EasyStock.Web/Views/Pedidos/Recibo.cshtml
