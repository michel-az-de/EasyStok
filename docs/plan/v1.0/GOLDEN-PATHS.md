# GOLDEN-PATHS.md — Fluxos E2E críticos v1.0

**Status:** Proposed (aguardando revisão do Felipe).
**Última atualização:** 2026-05-26.

33 golden paths que constituem o teste funcional do v1.0. Cada um cobre 1 feature DENTRO × 1 persona aplicável. Critério de **Marco Zero**: todos os paths PASS em staging na Fase 1, regredem 0 vezes nas Fases 2–5, e têm spec Playwright na Fase 4.

---

## Personas

| Cód | Persona | Papel |
|---|---|---|
| A | Admin Loja | dono ou gerente — cadastra, configura, vê relatórios |
| B | Vendedor / Operador Caixa | opera o dia a dia — venda, caixa, atendimento |
| C | Cliente Storefront | consumidor final B2C — navega, pede, paga |
| D | Sistema | jobs background, integrações, webhooks |

---

## Índice

| GP | Título | Feature | Persona |
|---|---|---|---|
| GP-001 | Login Admin | Auth | A |
| GP-002 | Login Vendedor (Web) | Auth | B |
| GP-003 | Login Cliente (OTP storefront) | Auth | C |
| GP-004 | Refresh token automático | Auth | D |
| GP-005 | Criar/configurar loja inicial | Loja | A |
| GP-006 | Cadastrar produto + categoria + variação | Produto+Cat | A |
| GP-007 | Atualizar preço e estoque inicial | Produto+Cat | A |
| GP-008 | Entrada de estoque manual | Estoque+Lote | A |
| GP-009 | Criar lote de produção | Estoque+Lote | A |
| GP-010 | Baixa automática via venda | Estoque+Lote | D |
| GP-011 | Cadastrar fornecedor | Fornecedor+Compras | A |
| GP-012 | Sugestão de compra → pedido fornecedor | Fornecedor+Compras | A |
| GP-013 | Receber mercadoria → atualizar estoque | Fornecedor+Compras | A |
| GP-014 | Cadastrar cliente | Cliente | A |
| GP-015 | Autocadastro cliente via storefront | Cliente | C |
| GP-016 | Criar pedido balcão completo | Pedido/Venda | B |
| GP-017 | Criar pedido via storefront | Pedido/Venda | C |
| GP-018 | Revisar/aprovar pedido | Pedido/Venda | A |
| GP-019 | Abrir caixa | Caixa | B |
| GP-020 | Registrar movimento (sangria/suprimento) | Caixa | B |
| GP-021 | Fechar caixa + ver relatório | Caixa | B/A |
| GP-022 | Pagar pedido balcão via PIX | Pagamento | B |
| GP-023 | Pagar pedido storefront via PIX | Pagamento | C |
| GP-024 | Reconciliação automática webhook Efi | Pagamento | D |
| GP-025 | Email de confirmação de pedido | Notificações | D |
| GP-026 | Push de status de pedido | Notificações | C |
| GP-027 | Configurar templates de notificação | Notificações | A |
| GP-028 | Configurar cardápio + zonas + janelas | Storefront | A |
| GP-029 | Pedido completo no storefront E2E | Storefront | C |
| GP-030 | Aprovar/recusar pedido storefront | Storefront | A |
| GP-031 | Criar conta a pagar | Financeiro | A |
| GP-032 | Dar baixa em conta a receber | Financeiro | D |
| GP-033 | Relatório financeiro do dia | Financeiro | A |

---

## Detalhamento

### GP-001 — Login Admin

**Feature:** Auth · **Persona:** A · **Crítico:** sim — sem isso ninguém configura nada.

**Pré-condição:** usuário admin existe (`Usuario.Perfil = Admin`), senha conhecida, email confirmado.

**Passos:**
1. Acessar `https://easystok-admin.fly.dev/Login`.
2. Inserir email + senha válidos.
3. Clicar em "Entrar".

**Resultado esperado:** redirect 302 para `/Dashboard` com cookie de sessão + JWT setados.

**Critério de sucesso E2E:**
- HTTP 200 final em `/Dashboard`.
- Cookie `AdminSession` presente e válido.
- Request subsequente em endpoint protegido retorna 200.
- Falha com senha errada retorna mensagem clara (não 500).

---

### GP-002 — Login Vendedor (Web)

**Feature:** Auth · **Persona:** B · **Crítico:** sim.

**Pré-condição:** usuário com `Perfil = Vendedor` ou `Caixa`, vinculado a uma `Loja`.

**Passos:**
1. Acessar `https://easystok-web.fly.dev/Login`.
2. Inserir credenciais.
3. Selecionar `Loja` ativa (se múltiplas).

**Resultado esperado:** sessão Web ativa, contexto de loja resolvido via claim.

**Critério de sucesso E2E:**
- Header `X-Loja-Id` propagado em todas as requests.
- Endpoint `/api/loja-atual` retorna a loja selecionada.
- Logout limpa cookie.

---

### GP-003 — Login Cliente (OTP storefront)

**Feature:** Auth · **Persona:** C · **Crítico:** sim — sem isso, storefront não converte.

**Pré-condição:** loja com storefront publicado, slug acessível, provider de OTP funcionando (Email — WhatsApp está em D-007).

**Passos:**
1. Acessar `https://<loja>.easystok.com/storefront/<slug>`.
2. Clicar em "Entrar" → inserir telefone ou email.
3. Receber OTP no canal (Email v1.0).
4. Digitar OTP e confirmar.

**Resultado esperado:** cookie `ClienteSession` setado via `ClienteSessionMiddleware` (sliding 30d, ADR-0019 / `ExpirarClienteSessionsBackgroundService`).

**Critério de sucesso E2E:**
- `POST /api/storefront/{slug}/auth/solicitar-otp` retorna 200.
- `POST /api/storefront/{slug}/auth/validar-otp` com OTP correto retorna 200 + cookie.
- Tentar com OTP errado 5x bloqueia temporariamente.
- OTP expira em 5min e retorna 410 Gone.

---

### GP-004 — Refresh token automático

**Feature:** Auth · **Persona:** D · **Crítico:** sim — sem isso, sessões caem mid-fluxo.

**Pré-condição:** JWT com expiry < 5min, refresh token válido.

**Passos:**
1. Cliente faz request com JWT prestes a expirar.
2. `TokenRefreshHandler` detecta proximidade do expiry.
3. Chama `/auth/refresh` automaticamente.
4. Nova JWT retorna no header de resposta.

**Resultado esperado:** request original processa com sucesso; cliente não percebe expiry.

**Critério de sucesso E2E:**
- Header `X-Refreshed-Token` presente na resposta.
- Request subsequente usa o novo token sem reautenticação.
- Refresh inválido → 401 + força login.

---

### GP-005 — Criar/configurar loja inicial (onboarding)

**Feature:** Loja · **Persona:** A · **Crítico:** sim.

**Pré-condição:** admin recém-cadastrado, sem `Loja` associada.

**Passos:**
1. Login no Admin (GP-001).
2. Wizard de onboarding: dados loja (nome, CNPJ, endereço, telefone, email).
3. Configurar `ConfiguracaoLoja` (impostos default, política de estoque).
4. Salvar.

**Resultado esperado:** `Loja` + `ConfiguracaoLoja` persistidos; admin associado como `LojaUsuario`.

**Critério de sucesso E2E:**
- `Loja.Ativo = true`.
- RLS Postgres (`current_setting('app.empresa_id')`) propaga em queries subsequentes.
- Tentar acessar dados de outra loja retorna 403.

---

### GP-006 — Cadastrar produto + categoria + variação

**Feature:** Produto+Cat · **Persona:** A · **Crítico:** sim.

**Pré-condição:** loja configurada (GP-005).

**Passos:**
1. Admin → Produtos → Novo.
2. Inserir nome, descrição, código (SKU), categoria (criar nova se não existir).
3. Adicionar variação (ex: tamanho M / G) com preço próprio.
4. Salvar.

**Resultado esperado:** `Produto` + `Categoria` + `VariacaoProduto` persistidos com vínculo correto.

**Critério de sucesso E2E:**
- Produto aparece na listagem.
- Cada variação tem SKU único.
- Mudança de categoria reflete em filtros.

---

### GP-007 — Atualizar preço e estoque inicial

**Feature:** Produto+Cat · **Persona:** A · **Crítico:** médio.

**Pré-condição:** produto cadastrado (GP-006).

**Passos:**
1. Admin → Produto → Editar.
2. Atualizar `PrecoVenda`.
3. Lançar estoque inicial (quantidade + lote opcional).

**Resultado esperado:** histórico de preço registrado; entrada de estoque movimentada.

**Critério de sucesso E2E:**
- Audit log (`EntityAlteracao`) registra mudança de preço.
- Saldo de estoque reflete entrada.
- Storefront usa novo preço imediatamente (sem cache stale).

---

### GP-008 — Entrada de estoque manual

**Feature:** Estoque+Lote · **Persona:** A · **Crítico:** sim.

**Pré-condição:** produto cadastrado.

**Passos:**
1. Admin → Estoque → Movimentação → Entrada.
2. Selecionar produto + variação + quantidade + custo unitário.
3. Vincular a `Fornecedor` (opcional) + `Lote` (opcional).
4. Salvar.

**Resultado esperado:** `MovimentoEstoque` persistido; saldo atualizado.

**Critério de sucesso E2E:**
- Saldo bate com soma de movimentos.
- Custo médio recalcula.
- Movimento aparece no histórico.

---

### GP-009 — Criar lote de produção

**Feature:** Estoque+Lote · **Persona:** A · **Crítico:** médio.

**Pré-condição:** produto com `RastreabilidadePorLote = true`.

**Passos:**
1. Admin → Lotes → Novo.
2. Selecionar produto, código do lote, data de produção, validade.
3. Vincular itens (`ItemLote`).
4. Finalizar lote.

**Resultado esperado:** `Lote` em estado `Finalizado`; itens disponíveis para venda.

**Critério de sucesso E2E:**
- Lote bloqueado para edição após finalização (regra de domínio).
- Saídas FIFO respeitam validade.

---

### GP-010 — Baixa automática via venda

**Feature:** Estoque+Lote · **Persona:** D · **Crítico:** sim.

**Pré-condição:** produto com estoque positivo; pedido confirmado (GP-016 ou GP-017).

**Passos:**
1. Pedido entra em status `Confirmado`.
2. Handler de domínio gera `MovimentoEstoque` de saída.
3. Se `RastreabilidadePorLote`, escolhe lote FIFO.

**Resultado esperado:** saldo decrementa; lote consome quantidade.

**Critério de sucesso E2E:**
- Sem estoque suficiente → pedido falha com mensagem clara (não 500).
- Cancelamento de pedido estorna estoque.

---

### GP-011 — Cadastrar fornecedor

**Feature:** Fornecedor+Compras · **Persona:** A · **Crítico:** médio.

**Pré-condição:** loja configurada.

**Passos:**
1. Admin → Fornecedores → Novo.
2. Inserir CNPJ, nome fantasia, contato, condição de pagamento default.
3. Vincular produtos fornecidos (opcional).

**Resultado esperado:** `Fornecedor` ativo.

**Critério de sucesso E2E:**
- CNPJ duplicado → erro claro.
- Edição preserva histórico (audit log).

---

### GP-012 — Sugestão de compra → pedido fornecedor

**Feature:** Fornecedor+Compras · **Persona:** A · **Crítico:** sim.

**Pré-condição:** produtos com `EstoqueMinimo` configurado, saldos abaixo do mínimo.

**Passos:**
1. Admin → Compras → Sugestão.
2. Sistema gera `SugestaoCompra` com itens sugeridos.
3. Admin revisa, ajusta quantidades.
4. Confirma → gera `PedidoFornecedor`.

**Resultado esperado:** `PedidoFornecedor` em status `EmAberto`.

**Critério de sucesso E2E:**
- `PreviewSugestaoCompra` retorna mesmas quantidades antes de salvar.
- Pedido tem `NumeroPedido` único e auditado.

---

### GP-013 — Receber mercadoria → atualizar estoque

**Feature:** Fornecedor+Compras · **Persona:** A · **Crítico:** sim.

**Pré-condição:** `PedidoFornecedor` em `EmAberto`.

**Passos:**
1. Admin → Pedido Fornecedor → Receber.
2. Confirmar quantidades recebidas (pode ser parcial).
3. Lançar custo real (pode diferir do orçado).
4. Confirmar recebimento.

**Resultado esperado:** `MovimentoEstoque` de entrada criado; status do pedido vira `Recebido` (ou `RecebidoParcial`).

**Critério de sucesso E2E:**
- Custo médio do produto recalcula com base no real.
- Conta a pagar é gerada em Financeiro (GP-031 vinculado).

---

### GP-014 — Cadastrar cliente (admin)

**Feature:** Cliente · **Persona:** A · **Crítico:** médio.

**Pré-condição:** loja configurada.

**Passos:**
1. Admin → Clientes → Novo.
2. Inserir nome, CPF/CNPJ, telefone, email.
3. Adicionar endereço(s) e telefone(s) extras.

**Resultado esperado:** `Cliente` + `ClienteDocumento` + `ClienteEndereco` + `ClienteTelefone` persistidos.

**Critério de sucesso E2E:**
- CPF/CNPJ validado por algoritmo (sem hit a Receita).
- Cliente acessível via storefront se ativar via OTP.

---

### GP-015 — Autocadastro cliente via storefront

**Feature:** Cliente · **Persona:** C · **Crítico:** sim.

**Pré-condição:** storefront público acessível.

**Passos:**
1. Cliente acessa storefront.
2. Clica em "Cadastrar".
3. Insere nome + telefone + email.
4. Confirma OTP (GP-003).
5. Completa endereço de entrega.

**Resultado esperado:** `Cliente` criado + `ClienteSession` ativa.

**Critério de sucesso E2E:**
- Cliente subsequentemente faz pedido (GP-017) sem refazer cadastro.
- Sessão persiste por 30d (sliding).
- Dados editáveis pelo próprio cliente em /minha-conta.

---

### GP-016 — Criar pedido balcão completo (vendedor)

**Feature:** Pedido/Venda · **Persona:** B · **Crítico:** sim.

**Pré-condição:** vendedor logado (GP-002), caixa aberto (GP-019), produtos com estoque.

**Passos:**
1. Web → Nova Venda.
2. Selecionar cliente (opcional).
3. Adicionar itens via leitor ou busca.
4. Aplicar desconto (se permissão).
5. Selecionar forma de pagamento → confirmar.

**Resultado esperado:** `Pedido` criado em status `Confirmado`; estoque baixa (GP-010); pagamento registra; comprovante imprime/exporta.

**Critério de sucesso E2E:**
- Totais batem (subtotal + desconto - acréscimo = total).
- Pedido aparece no relatório de caixa.
- Comprovante tem dados fiscais (mesmo sem NFe, v1.0).

---

### GP-017 — Criar pedido via storefront (cliente)

**Feature:** Pedido/Venda · **Persona:** C · **Crítico:** sim — é o fluxo core do diferencial v1.0.

Ver detalhamento completo em GP-029 (super-path do storefront).

**Pré-condição:** cliente autenticado (GP-003 ou GP-015), itens no carrinho, janela de entrega disponível.

**Critério de sucesso E2E:**
- `Pedido.Origem = Storefront`.
- Cliente recebe notificação (GP-025/026).
- Admin recebe alerta de novo pedido.

---

### GP-018 — Revisar/aprovar pedido (admin)

**Feature:** Pedido/Venda · **Persona:** A · **Crítico:** sim para storefront.

**Pré-condição:** pedido em status `AguardandoAprovacao` (storefront, GP-017).

**Passos:**
1. Admin → Pedidos → Pendentes.
2. Visualiza detalhe + pagamento + estoque.
3. Aprova OU recusa com motivo.

**Resultado esperado:** status vira `Aprovado` (→ separação) ou `Recusado` (→ estorno automático se pago).

**Critério de sucesso E2E:**
- Cliente recebe notificação do desfecho.
- Recusa estorna PIX automaticamente (via Efi API).

---

### GP-019 — Abrir caixa (vendedor)

**Feature:** Caixa · **Persona:** B · **Crítico:** sim.

**Pré-condição:** vendedor logado, sem caixa aberto no turno.

**Passos:**
1. Web → Caixa → Abrir.
2. Inserir saldo inicial.
3. Confirmar.

**Resultado esperado:** `Caixa` em status `Aberto` para o vendedor + turno.

**Critério de sucesso E2E:**
- Tentar abrir 2 caixas para mesmo vendedor → bloqueia.
- Outro vendedor pode abrir seu próprio caixa em paralelo.

---

### GP-020 — Registrar movimento (sangria/suprimento)

**Feature:** Caixa · **Persona:** B · **Crítico:** médio.

**Pré-condição:** caixa aberto.

**Passos:**
1. Caixa → Movimento → Sangria (ou Suprimento).
2. Inserir valor + motivo.
3. Confirmar.

**Resultado esperado:** `MovimentoCaixa` persistido; saldo previsto atualiza.

**Critério de sucesso E2E:**
- Movimento aparece no extrato.
- Não pode movimentar se caixa fechado.

---

### GP-021 — Fechar caixa + ver relatório

**Feature:** Caixa · **Persona:** B (fecha) / A (revisa) · **Crítico:** sim.

**Pré-condição:** caixa aberto com movimentos do turno.

**Passos:**
1. Caixa → Fechar.
2. Inserir saldo real conferido.
3. Sistema mostra diferença (sobra/falta).
4. Confirmar fechamento.
5. Admin acessa relatório do turno.

**Resultado esperado:** `Caixa` em status `Fechado`; relatório com totais por forma de pagamento + diferença.

**Critério de sucesso E2E:**
- Sangria + suprimento + vendas batem com saldo previsto.
- Relatório exportável (CSV/PDF mínimo).
- Histórico de caixas fechados navegável.

---

### GP-022 — Pagar pedido balcão via PIX

**Feature:** Pagamento · **Persona:** B · **Crítico:** sim.

**Pré-condição:** pedido em `EmFinalização`, Efi configurado.

**Passos:**
1. Forma de pagamento → PIX.
2. Sistema gera QR code via `IPagamentoOrchestrator` → Efi.
3. Cliente escaneia e paga.
4. Webhook Efi notifica recebimento (GP-024 em paralelo).
5. Pedido fecha automático.

**Resultado esperado:** `PaymentAttempt` em status `Pago`; pedido confirmado.

**Critério de sucesso E2E:**
- QR code expira em N minutos sem pagamento.
- Webhook chega em <30s do pagamento.
- Cancelamento de pedido com PIX recebido → opção de estornar.

---

### GP-023 — Pagar pedido storefront via PIX

**Feature:** Pagamento · **Persona:** C · **Crítico:** sim.

Idêntico a GP-022 mas via cliente storefront. Ver GP-029.

---

### GP-024 — Reconciliação automática webhook Efi (sistema)

**Feature:** Pagamento · **Persona:** D · **Crítico:** sim.

**Pré-condição:** pedido com PIX gerado (GP-022/023).

**Passos:**
1. Cliente paga PIX no banco.
2. Efi envia webhook para `/api/integrations/efi/webhook`.
3. Endpoint valida assinatura + idempotency.
4. `IPagamentoOrchestrator` atualiza `PaymentAttemptEvent`.
5. Pedido transita para `Pago`/`Confirmado`.

**Resultado esperado:** estado consistente entre Efi e EasyStok.

**Critério de sucesso E2E:**
- Webhook duplicado é detectado e ignorado (idempotency).
- Webhook com assinatura inválida retorna 403.
- `ContaReceberPixReconciliacaoJob` reconcilia diariamente entradas perdidas.

---

### GP-025 — Email de confirmação de pedido (sistema)

**Feature:** Notificações · **Persona:** D · **Crítico:** sim.

**Pré-condição:** pedido confirmado, cliente com email.

**Passos:**
1. Pedido vira `Confirmado`.
2. Domain event publicado.
3. `NotificacoesAvaliadorOrchestrator` avalia template + canal.
4. `NotificacoesColetorOrchestrator` enfileira.
5. `DispatcherLoopHostedService` envia via SMTP.

**Resultado esperado:** email entregue em <2min.

**Critério de sucesso E2E:**
- Email tem dados corretos (número pedido, total, items).
- Falha de SMTP enfileira retry (Polly).
- Tabela `Outbox` documenta evento.

---

### GP-026 — Push de status de pedido (cliente)

**Feature:** Notificações · **Persona:** C · **Crítico:** médio.

**Pré-condição:** cliente com PWA instalada + `WebPushSubscription` ativa.

**Passos:**
1. Status do pedido muda (ex: `Aprovado` → `EmPreparo` → `Pronto`).
2. Domain event dispara.
3. Notificação push enviada via `WebPush`.

**Resultado esperado:** notificação aparece no dispositivo do cliente.

**Critério de sucesso E2E:**
- Push respeita opt-in/opt-out do cliente.
- Cliente sem subscription ativa não recebe (sem erro).
- Click no push abre o pedido no storefront.

---

### GP-027 — Configurar templates de notificação (admin)

**Feature:** Notificações · **Persona:** A · **Crítico:** baixo.

**Pré-condição:** loja configurada.

**Passos:**
1. Admin → Notificações → Templates.
2. Editar template (ex: "Confirmação pedido").
3. Inserir placeholders ({{Cliente.Nome}}, {{Pedido.Total}}).
4. Preview com dados fake.
5. Salvar.

**Resultado esperado:** `Template` atualizado; próxima notificação usa novo conteúdo.

**Critério de sucesso E2E:**
- Placeholder inválido (sem fechamento) → erro de validação.
- Preview reflete fielmente o envio real.

---

### GP-028 — Configurar cardápio + zonas + janelas (admin)

**Feature:** Storefront · **Persona:** A · **Crítico:** sim — pré-requisito de GP-029.

**Pré-condição:** loja configurada, produtos cadastrados.

**Passos:**
1. Admin → Storefront → Configurar.
2. Definir slug do storefront (`<loja>.easystok.com/storefront/<slug>`).
3. Cardápio: arrastar produtos para categorias visíveis.
4. Zonas de entrega: desenhar polígonos no mapa + taxa por zona.
5. Janelas de entrega: dias da semana × intervalos horários disponíveis.
6. Bloqueios: feriados, lotação máxima.
7. Publicar.

**Resultado esperado:** storefront acessível público; `CardapioItem`, `FreteZona`, `JanelaEntrega`, `BloqueioEntrega` persistidos.

**Critério de sucesso E2E:**
- URL público responde 200.
- Mudanças refletem em <1min.
- Janela bloqueada não aparece para cliente.

---

### GP-029 — Pedido completo no storefront E2E (super-path)

**Feature:** Storefront · **Persona:** C · **Crítico:** **SIM — golden path nº 1 do v1.0**.

**Pré-condição:** GP-028 + GP-015 (cliente cadastrado).

**Passos:**
1. Cliente acessa storefront público.
2. Navega cardápio, filtra categoria, busca produto.
3. Adiciona itens ao carrinho com variações + observações.
4. Vai ao checkout.
5. Confirma endereço de entrega (cadastrado em GP-015).
6. Sistema valida zona de entrega + calcula frete.
7. Escolhe janela de entrega disponível.
8. Seleciona forma de pagamento → PIX.
9. Confirma → QR code Efi (GP-023).
10. Paga via app do banco.
11. Webhook Efi reconcilia (GP-024).
12. Pedido vira `AguardandoAprovacao`.
13. Admin aprova (GP-018).
14. Cliente recebe email (GP-025) + push (GP-026).
15. Status muda para `EmPreparo` → `Pronto` → `Entregue`.

**Resultado esperado:** todos os passos completam em <15min total (sem contar preparo real).

**Critério de sucesso E2E:**
- `CheckoutIdempotency` impede pedido duplicado se cliente clica 2x.
- Fora de zona → mensagem clara + sugestão de retirada.
- Janela cheia → não permite selecionar.
- Sem pagar em N min → carrinho expira; reservas de estoque liberam.
- Cliente vê status atualizado em tempo real (SSE ou polling).

---

### GP-030 — Aprovar/recusar pedido storefront

**Feature:** Storefront · **Persona:** A · **Crítico:** sim.

Ver GP-018 (mesma operação, contexto storefront).

**Adições específicas storefront:**
- Admin vê pedido com mapa + cliente + janela escolhida.
- Aprovação dispara notificação para cliente (GP-025/026).
- Recusa exige motivo + estorna PIX (GP-024).

---

### GP-031 — Criar conta a pagar

**Feature:** Financeiro · **Persona:** A · **Crítico:** médio.

**Pré-condição:** fornecedor cadastrado (GP-011).

**Passos:**
1. Admin → Financeiro → Contas a Pagar → Nova.
2. Vincular fornecedor + valor + vencimento + descrição.
3. Categorizar (despesa operacional, custo direto, etc.).
4. Salvar.

**Resultado esperado:** `ContaPagar` em status `EmAberto`.

**Critério de sucesso E2E:**
- Vencidas aparecem em alerta no Dashboard.
- Recebimento de pedido fornecedor (GP-013) cria conta a pagar automática.

---

### GP-032 — Dar baixa em conta a receber (a partir de pedido pago)

**Feature:** Financeiro · **Persona:** D · **Crítico:** sim.

**Pré-condição:** pedido pago via PIX (GP-022/023) → reconciliado (GP-024).

**Passos:**
1. Webhook Efi confirma pagamento.
2. Sistema cria `MovimentoCaixa` e marca `ContaReceber` (auto-criada do pedido) como `Liquidada`.

**Resultado esperado:** fluxo de caixa atualizado, conta a receber baixada.

**Critério de sucesso E2E:**
- Soma de contas liquidadas = soma de pedidos pagos no período.
- Reconciliação manual disponível para resíduos.

---

### GP-033 — Relatório financeiro do dia

**Feature:** Financeiro · **Persona:** A · **Crítico:** médio.

**Pré-condição:** movimentações no dia.

**Passos:**
1. Admin → Financeiro → Relatório do Dia.
2. Filtra por loja (se múltiplas) + intervalo.
3. Visualiza:
   - Entradas (vendas pagas + outros recebimentos)
   - Saídas (contas pagas, sangrias)
   - Saldo líquido
   - Por forma de pagamento.

**Resultado esperado:** relatório consistente com Caixa (GP-021).

**Critério de sucesso E2E:**
- Soma com fechamento de caixa do dia bate.
- Exportável (CSV mínimo).
- Diferença detectada gera alerta visual.

---

## Resumo por persona

| Persona | Paths críticos | Total |
|---|---|---|
| A — Admin Loja | GP-001, 005, 006, 008, 011, 012, 013, 014, 018, 027, 028, 030, 031, 033 | 14 |
| B — Vendedor | GP-002, 016, 019, 020, 021, 022 | 6 |
| C — Cliente Storefront | GP-003, 015, 017, 023, 026, 029 | 6 |
| D — Sistema | GP-004, 010, 024, 025, 032 | 5 |
| **Misto (B+A)** | GP-007, 009 | 2 |
| **Total** | | **33** |

---

## Critério de Marco Zero (resumo)

- Todos os 33 paths **PASS em staging** ao fim da Fase 1.
- Todos os P0 detectados na Fase 1 fechados na Fase 3.
- Todos os 33 com **spec Playwright** automatizada na Fase 4.
- Smoke synthetic na Fase 2 cobre **pelo menos 1 path por feature** rodando a cada 5min em produção.
