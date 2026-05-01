# Tech Debt — EasyStock

> Atualizar quando resolver. Ordenado por impacto.

## P0 — Bloqueia produção real

1. **`PedidoFornecedor.Itens` é `[NotMapped]`**
   - Compras "funcionam" no UI mas itens não persistem.
   - Recebimento de compra não dá entrada de estoque correta.
   - Fix: adicionar `PedidoFornecedorItem` entity + configuration EF + migration.

2. **Webhook Pix não valida valor recebido vs cobrança**
   - Vulnerabilidade R$0,01: pagador pode pagar centavo e ativar plano.
   - Fix: comparar `pix.valor` contra `cobranca.Valor` no `WebhookPixController`.

3. **`DiagnosticoController` ainda tem rotas perigosas**
   - Verificar: `ProxyLimparLogs`, `ProxyEsvaziarLixeira`, `ProxyDeleteContainer` precisam estar `[Authorize(Roles="SuperAdmin")]`.

## P1 — Confiabilidade

4. **Compras (recebimento) sem teste de integração**
   - Fluxo de entrada de estoque por compra é manual, sem cobertura.

5. **Sem teste E2E pro fluxo Pedido→Venda→Caixa**
   - Cobertura só unit. Bugs de integração escapam.

6. **`Infra.MongoDb` é parcial e divergente do Postgre**
   - `MongoUnitOfWork` tem fallback exótico pra "transação não suportada".
   - Decidir: mantém ou remove. Hoje é dead-ish code com superfície de bug.

## P2 — Qualidade

7. **PWA mobile tem 12,876 linhas de JS monolítico** (`casa-da-baba-mobile/pwa/sync.js`)
   - Sem testes. Sync offline complexo.
   - Não migrar agora, mas isolar mudanças.

8. **Falta migration formalizada para `xmin` em entidades novas**
   - xmin é system column, mas a config Fluent API precisa ser repetida — fácil esquecer ao adicionar entidade.

9. **`appsettings.Production.json` com placeholders não-óbvios**
   - `Efi:ClientId`, `Stripe:Webhook` etc. Onboarding novo dev confuso.

10. **`SubscriptionGateMiddleware` não tem cache**
    - Bate no DB toda request autenticada. Latência cumulativa.

## P3 — Cosmético / Future

11. Sem CI rodando os testes em PR.
12. Sem feature flags maduras (`Microsoft.FeatureManagement`).
13. Sem health-check estruturado (`/health/live`, `/health/ready`).
14. Status de pedido como string — adicionar enum-string converter pra evitar typos.
15. Logs sem `correlationId` propagado consistentemente.
