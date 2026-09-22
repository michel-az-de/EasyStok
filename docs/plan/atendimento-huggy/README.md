# Plano executável — backend do atendimento via Huggy (Casa da Baba)

Issue guarda-chuva: #1040 · Decisão: ADR-0049 · Origem: docs 01-04 em `C:\Users\felip\Downloads\`
Escopo: **somente backend** (`EasyStock.Api`, `Application`, `Domain`, `Infra.*`, `Worker`). O front do
operador é outra tecnologia e outro repositório. O site casadababa.com continua consumindo a API.

> Este plano foi escrito para ser executado **uma spec por sessão** por um modelo mais barato.
> Cada spec traz tudo o que a sessão precisa: arquivos a ler, arquivos a criar, testes a escrever
> primeiro e aceite verificável. Não é preciso explorar o repositório além da "Leitura mínima".

## Índice

| Onda | Arquivo | Specs | Entrega |
|---|---|---|---|
| 1 | [01-fundacao-huggy.md](01-fundacao-huggy.md) | S01–S09 | Huggy conectada, conversa persistida, agente respondendo, handoff, provider de WhatsApp via Huggy |
| 2 | [02-pedido-e-cobranca.md](02-pedido-e-cobranca.md) | S10–S16 | Pedido criado na conversa antes do pagamento, Pix conciliado, esteira sem aprovação manual, avisos de status |
| 3 | [03-esteira-e-cozinha.md](03-esteira-e-cozinha.md) | S17–S21 | Estoque avisa e não trava, SSE de operação, KDS sobre `Pedido`, canhoto e fila de impressão, atraso |
| 4 | [04-estoque-minimo.md](04-estoque-minimo.md) | S22–S23 | Alerta de desacerto com ajuste, produção em porções gerando estoque |
| 5 | [05-crm-e-pos-venda.md](05-crm-e-pos-venda.md) | S24–S27 | Tags, notas, bloqueio, preferências, dossiê sincronizado na Huggy, avaliação em 2 opções, ocorrência e reembolso |
| 6 | [06-campanhas.md](06-campanhas.md) | S28–S31 | Campanha com público filtrado, exclusões, limite semanal, ondas, lembrete, interesse |
| 7 | [07-poda.md](07-poda.md) | P01–P06 | Remoção do que é SaaS, fiscal, MAUI e alvos de deploy extras |

Ordem: 1 → 2 → 3 → 4 → 5 → 6. A onda 7 pode correr em paralelo a partir da onda 4 (P05 depende de S18/S19).

## Onda 0 — o que só o Felipe destrava (fora do código)

| # | Ação | Bloqueia |
|---|---|---|
| 0.1 | Criar app na Huggy (Configurações → Seus aplicativos): `client_id`, `client_secret`, URL de webhook, eventos `startedChat`, `receivedMessage`, `sentAllMessage`, `agentEntered`, `closedChat`, `createdCustomer`, `updatedCustomer` | S01, S03 |
| 0.2 | Levar o número de WhatsApp da Casa da Baba para a Huggy (a Huggy conduz a verificação da Meta) | S06 em produção |
| 0.3 | Criar na Huggy o departamento "Tatiana" (fila humana) e os campos personalizados de contato: `ultima_compra`, `favorito`, `tags`, `bloqueado`, `ficha_url` | S07, S25 |
| 0.4 | Credenciais Efi Pix de produção (`Efi:ClientId`, `Efi:ClientSecret`, `Efi:ChavePix`, `Efi:WebhookSecret`) e URL do webhook Pix cadastrada na Efi | S11 |
| 0.5 | Chave Anthropic (`Anthropic:ApiKey`) | S06 |
| 0.6 | Modelo da impressora térmica (USB, Bluetooth, rede, ou com polling em nuvem) | S20 (decide o consumidor da fila) |
| 0.7 | Ligar `ENABLE_VIACEP_LOOKUP` e `ENABLE_NOMINATIM_GEOCODING` no ambiente | S14 |
| 0.8 | Templates HSM aprovados na Huggy/Meta: `pedido_em_preparo`, `pedido_saiu`, `pedido_entregue`, `avaliacao`, `campanha_generica` | S13, S26, S30 fora da janela de 24 h |

## Convenções do executor (vinculantes, resumo do CLAUDE.md v4.0)

1. **Fluxo por spec:** `gh issue create` (título = título da spec; body = Problema, Abordagem, Escopo, Aceite, Rollback copiados da spec; labels da spec) → worktree `C:\rep\.worktrees\EasyStok\<slug>` com branch `feat/<slug>-<N>` a partir de `origin/master` → Red → Green → gate → commit → push → PR `closes #N`.
2. **Gate:** `powershell -File scripts/poka-yoke/gate.ps1` verde antes de cada commit (é o mesmo do pre-commit).
3. **Commits:** `tipo(escopo): descrição imperativa` + corpo `refs #N`. Stage arquivo por arquivo. Nunca `git add .`.
4. **Tier:** `feat`, `refactor`, migration, auth = **alto** (PR fica aberta até a label `aprovado`). `docs`, `test`, `chore`, `fix` trivial = baixo (auto-merge no verde).
5. **Código:** nomes de negócio em PT-BR com sufixos técnicos em inglês (ADR-0011); use case implementa `IUseCase<TIn,TOut>` com `CancellationToken` (ADR-0013); controller devolve `ActionResult<T>` (ADR-0019); evento vai para o outbox **antes** do único `CommitAsync` (ADR-0030); `EmpresaId` no `WHERE` de toda query nova além do RLS (ADR-0010); migration gerada com `dotnet ef migrations add` e `.Designer.cs` (ADR-0024); migration aplicada em produção **antes** de ligar configuração que dependa dela.
6. **Testes:** Red primeiro, com o nome listado na spec. Projetos: `EasyStock.Domain.Tests`, `EasyStock.Application.Tests`, `EasyStock.Api.UnitTests`, `EasyStock.Api.IntegrationTests` (Postgres via Testcontainers, fora do `CI.slnf`: rodar local), `EasyStock.ArchitectureTests` (roda no gate).
7. **Não faça:** refatorar fora do Escopo; ler além da Leitura mínima; abrir o `graphify`; tocar `EasyStok.Mobile`, `EasyStock.Admin` ou `EasyStock.Web` salvo quando a spec mandar (R8: assinatura pública estendida = todos os call-sites no mesmo commit).
8. **Stack real:** .NET 10 (`global.json` 10.0.201), EF Core, Postgres. O `CLAUDE.md` diz .NET 8 e o `README.md` diz 9: estão errados, corrigidos em P06.
9. **Segredos:** nunca em código, issue, log ou teste. Nomes de configuração, sim.

## Prompt de execução (copiar para a sessão do modelo executor)

```
Você vai executar a spec <ID> de docs/plan/atendimento-huggy/<arquivo>.md no repositório C:\rep\EasyStok.
1. Leia CLAUDE.md (§0 a §2) e docs/plan/atendimento-huggy/README.md (só "Convenções do executor").
2. Leia a spec <ID> inteira. Depois leia SOMENTE os arquivos da "Leitura mínima" dela.
3. Rode o inventário do §0 do CLAUDE.md. Abra a issue e o worktree conforme a convenção 1.
4. Escreva primeiro os testes listados em "Testes (Red)". Rode-os. Confirme que falham pelo motivo esperado.
5. Implemente o mínimo para ficar verde. Não toque em nada fora de "Escopo".
6. gate.ps1 verde; commits por fatia; push; PR com closes #N e o tier indicado na spec.
7. Feche com: Veredito, Feito, Evidência (comandos e testes rodados), Falta, Próximo passo.
Se algo da spec contradisser o código, PARE e reporte a divergência com arquivo:linha antes de decidir.
```

Economia de tokens: uma spec por sessão, contexto zerado entre specs, leitura restrita à lista. O custo
de exploração já foi pago na análise (doc 04, 432 k tokens de subagentes); não repita.

## Vocabulário de caminhos usados nas specs

| Apelido | Caminho |
|---|---|
| Domain | `EasyStock.Domain/` |
| App | `EasyStock.Application/` |
| Api | `EasyStock.Api/` |
| Postgre | `EasyStock.Infra.Postgre/` |
| Integrations | `EasyStock.Infra.Integrations/` |
| Notifications | `EasyStock.Infra.Notifications/` |
| Async | `EasyStock.Infra.Async/` |
| Worker | `EasyStock.Worker/` |

## Fatos medidos que as specs assumem (22/09/2026)

- Huggy API v3: base `https://api.huggy.app/v3`, OAuth 2.0 authorization code (`https://auth.huggy.app/oauth/authorize` → `.../oauth/access_token`, token de 6 meses com refresh), header `Authorization: Bearer`. Endpoints: `POST /chats/{id}/messages` (`text`, `file`/`fileBase64`, `hsm`), `GET /chats`, `GET /chats/{id}`, `POST /contacts`, `GET|PUT /contacts/{id}` (`custom_fields` na resposta), `POST /chats/{id}/transfer` (`agentId`, `message`), `PUT /chats/{id}/department` (`department`), `PUT /chats/{id}/close`. Webhook: configurado em Configurações → Webhook ou pelo app; handshake `POST` com `token` e `validToken:true`, o sistema responde o token; sem assinatura; eventos `startedChat`, `receivedMessage` (só chat em fila ou automático), `receivedAllMessage`, `sentAllMessage`, `agentEntered`, `closedChat`, `createdCustomer`, `updatedCustomer`, `leftQueue`, `updatedChatDepartment`. Payload de `receivedMessage`: `id`, `body`, `chat.id`, `chat.channel`, `chat.situation`, `sender.*`, `customer.{id,name,mobile,email}`, `send_at`, `file`, `is_internal`. Fonte: `developers.huggy.io/pt/API/api-v3.html` e `/pt/webhook/webhook.html`.
- O KDS atual (`Api/Mobile/Controllers/KdsController.cs`) consulta a entidade espelho `Domain/Entities/Mobile/Order`, não `Pedido`. O SSE (`Api/Mobile/Services/MobileEventBroker.cs`) publica `order.upsert` e `order.ready` a partir do sync mobile.
- Não existe job que cancele `AguardandoPagamento` após 30 min, apesar do comentário em `IniciarCheckoutUseCase.cs:31`.
- `PedidoStateMachine.AceitaPagamento` recusa pagamento em `Rascunho`, `AguardandoPagamento` e `AguardandoAprovacaoBaba`: o webhook precisa transitar o status antes de registrar o pagamento.
- `PedidoEstoqueIntegrationService.cs:108-118` lança `EstoqueInsuficienteException` e aborta a transição para `Pronto` quando falta saldo (`Pedidos:PermiteEstoqueNegativo=false` por padrão); com `true`, só desconta o que há e não grava descoberto.
- `FinalizarLoteUseCase` gera etiquetas e **não** cria `ItemEstoque`. `NaturezaMovimentacaoEstoque.Producao` existe.
- `ConsentimentoNotificacao` e `PreferenciaNotificacaoUsuario` são por **usuário do ERP**; o cliente final só tem `Cliente.ConsentiuMarketing`.
- `WebhookPixController.ProcessarPagamentoAsync` (linha 137) roteia por prefixo do `txid` (`cr` = parcela a receber; demais = cobrança de assinatura).
- Cliente Claude existente: `Infra.Postgre/Services/GeradorAutoPreenchimentoClaude.cs` (`POST https://api.anthropic.com/v1/messages`, headers `x-api-key` e `anthropic-version: 2023-06-01`, modelo `claude-haiku-4-5-20251001`).
