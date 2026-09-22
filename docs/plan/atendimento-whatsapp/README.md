# Plano executável — backend do atendimento por WhatsApp (Casa da Baba)

Issues: #1040 (plano original) e #1042 (revisão para Meta direto) · Decisão: ADR-0050 (supersede ADR-0049) · Origem: docs 01-04 em `C:\Users\felip\Downloads\`
Escopo: **somente backend** (`EasyStock.Api`, `Application`, `Domain`, `Infra.*`, `Worker`). O front do
operador é outra tecnologia e outro repositório. O site casadababa.com continua consumindo a API.

> Este plano foi escrito para ser executado **uma spec por sessão** por um modelo mais barato.
> Cada spec traz tudo o que a sessão precisa: arquivos a ler, arquivos a criar, testes a escrever
> primeiro e aceite verificável. Não é preciso explorar o repositório além da "Leitura mínima".

## Índice

| Onda | Arquivo | Specs | Entrega |
|---|---|---|---|
| 1 | [01-fundacao-whatsapp.md](01-fundacao-whatsapp.md) | S01–S09 | Meta Cloud API conectada, conversa persistida, agente respondendo, handoff pelo console, provider de notificações com janela de 24 h |
| 2 | [02-pedido-e-cobranca.md](02-pedido-e-cobranca.md) | S10–S16 | Pedido criado na conversa antes do pagamento, cobrança do Mercado Pago conciliada, esteira sem aprovação manual, avisos de status |
| 3 | [03-esteira-e-cozinha.md](03-esteira-e-cozinha.md) | S17–S21 | Estoque avisa e não trava, SSE de operação, KDS sobre `Pedido`, canhoto e fila de impressão, atraso |
| 4 | [04-estoque-minimo.md](04-estoque-minimo.md) | S22–S23 | Alerta de desacerto com ajuste, produção em porções gerando estoque |
| 5 | [05-crm-e-pos-venda.md](05-crm-e-pos-venda.md) | S24–S27 | Tags, notas, bloqueio, preferências, dossiê, avaliação em 2 botões, ocorrência e reembolso |
| 6 | [06-campanhas.md](06-campanhas.md) | S28–S31 | Campanha com público filtrado, exclusões, limite semanal, ondas, lembrete, interesse |
| 7 | [07-poda.md](07-poda.md) | P01–P06 | Remoção do que é SaaS, fiscal, MAUI e alvos de deploy extras |
| MP | [08-mercado-pago.md](08-mercado-pago.md) | S32–S33 | Cadastro da integração com o Mercado Pago (checklist externo, chaves exatas), processor do webhook que hoje não existe, estorno; opção A adotada: gateway único |

Ordem: 1 → 2 → 3 → 4 → 5 → 6. S32 (Mercado Pago, doc 08) entra logo após S11. A onda 7 pode correr em paralelo a partir da onda 4 (P05 depende de S18/S19).

## Onda 0 — o que só o Felipe destrava (fora do código)

| # | Ação | Bloqueia |
|---|---|---|
| 0.1 | Verificação da empresa na Meta (bloqueio casa-da-baba #6) e app no `developers.facebook.com` com o produto WhatsApp; número da Casa da Baba registrado na Cloud API com nome de exibição aprovado. O número sai do app WhatsApp Business do celular; verificar antes se a "coexistência" app + API está disponível no Brasil | S03 em produção |
| 0.2 | Usuário de sistema com token permanente (`Notifications:WhatsApp:Meta:AccessToken`), `AppSecret` do app, `VerifyToken` gerado por você, e a URL `https://<api>/api/webhooks/whatsapp` assinada no campo `messages` do webhook | S01, S03 |
| 0.3 | Templates aprovados no WhatsApp Manager, idioma `pt_BR`: `pedido_pago`, `pedido_em_preparo`, `pedido_saiu`, `pedido_entregue` (utilidade), `avaliacao` (utilidade, com 2 botões de resposta rápida), `campanha_generica` (marketing, com imagem no cabeçalho) | S13, S26, S30 fora da janela de 24 h |
| 0.4 | Efi Pix deixa de bloquear o pedido (opção A: gateway único Mercado Pago). Credenciais da Efi só para contas a receber da FMA | nada do atendimento |
| 0.5 | Chave Anthropic (`Anthropic:ApiKey`) | S06 |
| 0.6 | Modelo da impressora térmica (USB, Bluetooth, rede, ou com polling em nuvem) | S20 (decide o consumidor da fila) |
| 0.7 | Ligar `ENABLE_VIACEP_LOOKUP` e `ENABLE_NOMINATIM_GEOCODING` no ambiente | S14 |
| 0.8 | Storage S3 compatível configurado (`FileStorage:S3:*`, já existe) para a mídia recebida, e `PublicBaseUrl` para a imagem do cardápio | S02, S06 |
| 0.9 | Mercado Pago: conta, aplicação Checkout Pro, credenciais de teste e produção, webhook com assinatura secreta, usuários de teste (roteiro completo em [08-mercado-pago.md](08-mercado-pago.md); fecha casa-da-baba #10) | S32, S33 e o checkout do site |

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
Você vai executar a spec <ID> de docs/plan/atendimento-whatsapp/<arquivo>.md no repositório C:\rep\EasyStok.
1. Leia CLAUDE.md (§0 a §2) e docs/plan/atendimento-whatsapp/README.md (só "Convenções do executor" e "Fatos medidos").
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

- **Meta Cloud API.** Envio: `POST https://graph.facebook.com/{versao}/{PHONE_NUMBER_ID}/messages` com `messaging_product: "whatsapp"`, `to` = `wa_id` (dígitos E.164, sem `+`) e `type` = `text` (`text.body`), `image` (`image.link` público em HTTPS ou `image.id`, `caption`), `interactive` (`interactive.type: "button"`, `body.text`, `action.buttons[] { type: "reply", reply: { id, title } }`, até 3 botões, título ≤ 20 caracteres), `template` (`template.name`, `template.language.code: "pt_BR"`, `template.components[] { type: "body", parameters[] { type: "text", text } }`). Marcar como lida: mesmo endpoint com `status: "read"` e `message_id`. Resposta traz `messages[0].id` (`wamid`). Mídia recebida: `GET /{MEDIA_ID}` devolve `url` temporária (5 minutos) e `mime_type`; o binário exige `Authorization: Bearer`. Erro `131047` = mensagem fora da janela de 24 h sem template. Janela de atendimento: 24 h a partir da última mensagem do cliente; fora dela só template aprovado. Atendimento iniciado pelo cliente não é cobrado; templates de utilidade e marketing são cobrados por mensagem, marketing é o mais caro. Fonte: `developers.facebook.com/docs/whatsapp/cloud-api`.
- **Webhook da Meta.** `GET` de verificação com `hub.mode=subscribe`, `hub.verify_token` e `hub.challenge` (responder o challenge em texto puro, 200). `POST` com header `X-Hub-Signature-256: sha256=<HMAC-SHA256 do corpo bruto com o App Secret>`. Corpo: `entry[].changes[].value` com `metadata.phone_number_id`, `contacts[] { profile.name, wa_id }`, `messages[] { from, id, timestamp, type, text.body, image.id, interactive.button_reply { id, title }, button.payload, ... }` e `statuses[] { id, status (sent | delivered | read | failed), recipient_id, errors[] }`. Responder 200 rápido; a Meta reenvia quando não recebe 200.
- O provider `Notifications/WhatsApp/MetaCloudWhatsAppProvider.cs` já envia texto pela Cloud API (`graph.facebook.com/v19.0`) e é registrado como `whatsapp:meta`; `MetaCloudWhatsAppOptions` tem `AccessToken`, `PhoneNumberId`, `BaseUrl`. Não existe inbound, conversa nem agente.
- O KDS atual (`Api/Mobile/Controllers/KdsController.cs`) consulta a entidade espelho `Domain/Entities/Mobile/Order`, não `Pedido`. O SSE (`Api/Mobile/Services/MobileEventBroker.cs`) publica `order.upsert` e `order.ready` a partir do sync mobile.
- Não existe job que cancele `AguardandoPagamento` após 30 min, apesar do comentário em `IniciarCheckoutUseCase.cs:31`.
- `PedidoStateMachine.AceitaPagamento` recusa pagamento em `Rascunho`, `AguardandoPagamento` e `AguardandoAprovacaoBaba`: o webhook precisa transitar o status antes de registrar o pagamento.
- `PedidoEstoqueIntegrationService.cs:108-118` lança `EstoqueInsuficienteException` e aborta a transição para `Pronto` quando falta saldo (`Pedidos:PermiteEstoqueNegativo=false` por padrão); com `true`, só desconta o que há e não grava descoberto.
- `FinalizarLoteUseCase` gera etiquetas e **não** cria `ItemEstoque`. `NaturezaMovimentacaoEstoque.Producao` existe.
- `ConsentimentoNotificacao` e `PreferenciaNotificacaoUsuario` são por **usuário do ERP**; o cliente final só tem `Cliente.ConsentiuMarketing`.
- `WebhookPixController.ProcessarPagamentoAsync` (linha 137) roteia por prefixo do `txid` (`cr` = parcela a receber; demais = cobrança de assinatura).
- Web Push já existe: `Notifications/Push/WebPushCanal.cs`, `Domain/Entities/Notifications/WebPushSubscription.cs`, `Api/Controllers/PwaPushController.cs` (inscrição). Storage: `Async/Storage/S3CompatibleFileStorage.cs` e `LocalFileStorage.cs`.
- Cliente Claude existente: `Infra.Postgre/Services/GeradorAutoPreenchimentoClaude.cs` (`POST https://api.anthropic.com/v1/messages`, headers `x-api-key` e `anthropic-version: 2023-06-01`, modelo `claude-haiku-4-5-20251001`).
- **Mercado Pago nunca esteve vivo.** A preferência é criada (`MercadoPagoClient.cs:48`, endpoint `v1/payments/preferences`, suspeito), a assinatura do webhook é validada, mas **não existe `IGatewayWebhookProcessor` para o MP**: `POST /api/webhooks/mercadopago` grava `WebhookRecebido(Sucesso=false)` e responde 500 (`WebhookGatewayController.cs:108-115`). Não há consulta de pagamento nem estorno; nenhum alvo de deploy tem `MercadoPago__*`. Detalhe e correção em `08-mercado-pago.md`.
