# Glossário do Domínio — EasyStock

## Pedido vs Venda
- **Pedido**: documento operacional de cliente. Tem fluxo (`aguardando → preparando → pronto → entregue`) e pode ser cancelado.
- **Venda**: registro fiscal/financeiro derivado. Pode ser gerada na entrega ou em fluxo separado (PDV).
- Hoje a maior parte do estoque é descontado **no Pedido** (status `pronto`), não na Venda. Venda existe principalmente pra Caixa/Relatório.

## MovimentacaoEstoque
- Tabela append-only: cada saída/entrada é um registro novo. Saldo = soma das movimentações por `ItemEstoque`.
- `Natureza`: `Entrada | Saida | Venda | Devolucao | AjusteManual | Inventario | Compra | Transferencia`.
- `DocumentoReferencia` é a chave de idempotência. Formato: `"{pedidoId}:{itemId}"` para venda de pedido, `"compra:{compraId}"` pra entrada de compra, etc.
- `ExisteReferenciaAsync(empresaId, produtoId, ref, natureza)` evita duplicar movimentação.

## ItemEstoque
- Saldo por (Empresa, Loja, Produto). Multi-loja é first-class.
- `QuantidadeAtual` é `Quantidade` (VO) — wrap em `decimal`, valida não-negativo dependendo de config.

## Pedido → Estoque (fluxo)
1. Pedido criado em `aguardando` → estoque não muda.
2. Status vai pra `preparando` → ainda não muda.
3. Status vai pra `pronto` → `PedidoEstoqueIntegrationService.DescontarAsync(pedido)`:
   - Para cada item: busca `ItemEstoque` da loja, desconta `Quantidade`, cria `MovimentacaoEstoque(Natureza=Venda)`.
   - Idempotência: se já existe movimentação com a mesma DocumentoReferencia, pula.
4. `entregue` é só status final, não mexe estoque.
5. `cancelado` partindo de status que já descontou → cria movimentação `Devolucao` com mesma DocumentoReferencia + `:cancel`.

## Compras (PedidoFornecedor)
- Documento de compra ao fornecedor. Quando "Recebido" → entra `MovimentacaoEstoque(Natureza=Compra)`.
- ⚠️ `PedidoFornecedor.Itens` é `[NotMapped]` hoje — itens não persistem no banco. Tech-debt prioritário.

## Caixa
- Sessão (`Caixa`) com abertura/fechamento + lançamentos de `MovimentoCaixa`.
- Vendas, sangrias, suprimentos. Reconciliação por sessão.

## Assinatura
- `AssinaturaEmpresa` 1:1 com `Empresa`. Status: `Ativa | Trial | Suspensa | Cancelada`.
- `TrialFim`/`DataFim`. Job diário gera `CobrancaAssinatura` (Pix) 3 dias antes do vencimento.
- `SubscriptionGateMiddleware` retorna 402 se Suspensa em rotas de negócio.

## Plano
- `Plano` define limites: `LimiteUsuarios`, `LimiteLojas`, `LimiteProdutos`, `LimiteGeracoesIaMes`, etc. `0` = ilimitado em alguns campos (Demo Comercial).
- Violação dispara `PlanoLimiteAtingidoException(recurso)` → 402 com payload `{ recurso }`.

## IA (Anúncios/Conteúdo)
- Geração de descrição de produto e anúncios. Conta consumo mensal por empresa em `IaUsoMensal`.
- Limite mensal vem do plano. Reset 1º do mês.

## PWA Mobile (Casa da Babá)
- White-label fixado. Config por empresa (`MobileTenantConfig`). PWA vanilla JS em `EasyStock.Api/wwwroot/pwa/`.
- Sync offline via IndexedDB + service worker. Mantida como referência — não migrar pra framework agora.
