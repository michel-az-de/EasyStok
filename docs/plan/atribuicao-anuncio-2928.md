# Spec: registrar de qual anúncio veio cada pedido (#2928)

- Data: 2026-11-19
- Origem: estudo de ads do Atlas (michel-az-de/atlas#118, `01-rodada2-gestao-de-campanhas.md`) e banca R2 de utilidade do plano
- Tier: alto (migração e `feat` nova). Esta spec, depois de mergeada, dá às fatias S1 a S6 o tier baixo da ADR-0055
- Estado: proposta com as 3 decisões da seção 7 tomadas; aguarda a label `aprovado`
- Repositórios: EasyStok (S1, S2, S4, S5, S6) e casa-da-baba (S3, PR própria)

## 1. Problema medido

Hoje nenhuma venda pode ser ligada ao anúncio que a trouxe. Sem isso, Google Ads e Meta otimizam por clique e não por venda, e a Casa da Baba não sabe qual campanha paga a si mesma.

| Ponto da jornada | O que existe | O que se perde | Fonte |
|---|---|---|---|
| Site guarda a campanha | `analytics-context.js` guarda UTMs e ids de clique (`gclid`, `gbraid`, `wbraid`, `fbclid` e outros) em `sessionStorage` `cdb_campaign_v1`, 30 min, respeitando o consentimento | Nada sai do navegador para o EasyStok | casa-da-baba `src/ui/analytics-context.js:18-36` |
| Site abre o WhatsApp | `whatsapp-origem.js` escreve "Vim pelo anúncio do X" no texto | Descarta os ids de propósito (`:9-11`, `:50-52`) e não consulta o consentimento | casa-da-baba `src/ui/whatsapp-origem.js` |
| Chat do site | `POST /api/public/chat/{slug}/sessoes` cria `SessaoChatSite` e `Conversa` | O corpo é `{}`; `SessaoChatSite` não tem campo de origem | `ChatSiteController.cs:25`, `ConversaChatSiteService.cs:45` |
| Anúncio Click-to-WhatsApp | A Meta manda `referral` (`source_id`, `source_type`, `source_url`, `headline`, `ctwa_clid`) na primeira mensagem | `WebhookWhatsAppParser` não lê `referral`; `MensagemRecebidaWhatsApp` não tem o campo. O JSON bruto chega em `EntradaWhatsApp.RawJson` | `EventoWhatsApp.cs:1-33`, `ProcessarEventoWhatsAppUseCase.cs:286` |
| Conversa | `Conversa` tem `Canal` e `ContextoJson` | Nenhum campo de origem de marketing | `Conversa.cs:22-99` |
| Pedido | `Pedido.Origem` diz o sistema (`storefront`, `whatsapp`, `web`, `balcao`); `Pedido.Canal` diz o canal marcado à mão (#409) | Nenhum campo de campanha; `Pedido` não tem `ConversaId` (o vínculo é `Conversa.PedidoEmAndamentoId`) | `Pedido.cs:89,95`, `Conversa.cs:68` |
| Checkout do storefront | `IniciarCheckoutInput` e `IniciarCheckoutGuestInput` | Não recebem campanha | `UseCases/Storefront/Checkout/` |
| Envio de conversão | Só no navegador (`gtag`, `fbq`, `purchase-event.js`) | Nada no servidor | casa-da-baba `src/ui/analytics.js` |

Linha de base: 0% dos pedidos com campanha identificada (nenhuma coluna existe). Meta da fase: medir quantos pedidos de origem paga aparecem por semana; a meta numérica só é fixada depois de 4 semanas de dado.

## 2. Achados que moldam a solução

1. **O site já faz a parte difícil.** A captura, a validação (`valorSeguro`) e a regra de consentimento (UTM com `analytics` ou `marketing`; id de clique só com `marketing`) já existem em `analytics-context.js`. A spec reaproveita `campanhaAtual()`; não cria captura nova.
2. **O link `wa.me` não leva dado estruturado.** O único canal é o texto da mensagem, que a cliente pode apagar. Por isso o caminho do site para o WhatsApp precisa de decisão (seção 7, D1).
3. **O anúncio Click-to-WhatsApp é o caminho mais confiável.** A Meta entrega o `referral` no webhook, sem depender do navegador nem de consentimento de cookie. É o que mais importa para a Casa da Baba, que vende pelo WhatsApp.
4. **O pedido não aponta para a conversa.** A origem precisa ser copiada para o pedido no momento em que ele nasce, senão o relatório depende de junções frágeis por cliente e data.
5. **`06-campanhas.md` trata de disparo, não de anúncio.** O `CampanhaDestinatario.PedidoId` da S30 é atribuição de campanha outbound. Esta spec não mexe nele; os nomes evitam colisão (`OrigemAnuncio`, não `Campanha`).

## 3. Abordagem

```
Site (consentimento)                WhatsApp (anúncio CTWA)
campanhaAtual() ──► POST sessoes    webhook ── referral ──┐
              └──► POST checkout                          │
                        │                                 ▼
                        ▼                         Conversa.OrigemAnuncio
               Pedido.OrigemAnuncio ◄── copiada ao criar o pedido
                        │
                        ▼
          Relatório: pedidos e valor por campanha
```

**Value object `OrigemAnuncio`** (Domain, PT-BR conforme ADR-0011), gravado como owned type em colunas, não em `ContextoJson`, porque o relatório filtra e agrupa por ele:

| Campo | Tipo | Exemplo | Regra |
|---|---|---|---|
| `Plataforma` | string(20) | `google`, `meta`, `outra` | Derivada do id de clique ou de `utm_source` |
| `Via` | string(20) | `anuncio_whatsapp`, `site`, `site_chat` | Por onde a origem entrou |
| `Fonte`, `Meio`, `Campanha`, `Conteudo`, `Termo` | string(120) | `utm_*` | Texto validado, cortado em 120 |
| `IdAnuncio` | string(60) | `source_id` do `referral` | Só CTWA |
| `TituloAnuncio` | string(120) | `headline` do `referral` | Só CTWA |
| `TipoIdClique` | string(10) | `gclid`, `gbraid`, `wbraid`, `fbclid`, `ctwa_clid` | Um só, por prioridade |
| `IdClique` | string(200) | valor cru | Podado em 90 dias (S6) |
| `CapturadaEm` | timestamptz | | Momento do clique ou da mensagem |

Regras:

- **Atribuição:** último anúncio (D2). Um `referral` novo numa conversa substitui a origem anterior; o pedido guarda a origem que valia quando nasceu e não muda depois.
- **Consentimento:** o servidor confia na regra do site, mas valida de novo: sem a flag `marketing` no corpo, descarta `IdClique` e `TipoIdClique`.
- **Sem anúncio:** `OrigemAnuncio` nula. O relatório mostra "Sem origem de anúncio". Nada muda para quem não veio de anúncio.
- **Multi-tenant:** as colunas ficam em `conversas` e `pedidos`, que já têm `EmpresaId`, filtro global e RLS (ADR-0010). Nenhuma tabela nova.

## 4. Fatias

| S | Repo | Entrega | Arquivos principais | Teste (Red primeiro) |
|---|---|---|---|---|
| S1 | EasyStok | VO `OrigemAnuncio`, owned type em `Conversa` e `Pedido`, migração `OrigemDoAnuncio` (colunas anuláveis, sem backfill) | `EasyStock.Domain/ValueObjects/`, `Conversa.cs`, `Pedido.cs`, `EasyStock.Infra.Postgre/Migrations/` | `EasyStock.Domain.Tests`: normalização, corte em 120, prioridade do id de clique, descarte sem consentimento |
| S2 | EasyStok | Parser lê `referral`; `MensagemRecebidaWhatsApp.Referral`; `GravarEntradaAsync` grava a origem na conversa | `WebhookWhatsAppParser.cs`, `EventoWhatsApp.cs`, `ProcessarEventoWhatsAppUseCase.cs` | `EasyStock.Application.Tests` com payload real de CTWA (fixture) e sem `referral` |
| S3 | casa-da-baba | Chat do site e checkout enviam `campanha` (de `campanhaAtual()`) e a flag `marketing`; `whatsapp-origem.js` passa a consultar o consentimento | `src/api/chat.js`, checkout, `src/ui/whatsapp-origem.js` | Teste unitário do front: sem consentimento não envia id |
| S4 | EasyStok | `POST sessoes` e os dois checkouts aceitam `campanha` opcional; a origem é copiada da conversa ou do checkout para o pedido em todo ponto que cria pedido | `ChatSiteController.cs`, `IniciarCheckout*Input.cs`, `CriarPedidoAtendimentoUseCase`, `IniciarCheckout*UseCase`, `AprovarPedidoStorefrontUseCase` | `EasyStock.Api.IntegrationTests`: pedido nascido de conversa com `referral` sai com a origem; corpo antigo sem `campanha` continua 200 |
| S5 | EasyStok | `GET api/relatorios/pedidos-por-anuncio?de&ate`: pedidos, valor e ticket por plataforma e campanha, com a linha "Sem origem de anúncio", e tela de tabela simples no console (D3) | controller de relatórios, query, tela do console | Integração com 3 pedidos (Google, Meta, sem origem) |
| S6 | EasyStok | Job diário anula `IdClique` com mais de 90 dias; mantém campanha e plataforma | job do `Infra.Async` | Teste do job com relógio fixo |

Ordem: S1, depois S2 e S4 em paralelo, S3 depois de S4 no ar, S5 e S6 por último. Fora do escopo: subir conversão offline ao Google Ads e à Conversions API da Meta. Fica para a issue seguinte, que depende desta.

## 5. LGPD e riscos

| Risco | Tratamento |
|---|---|
| Id de clique é dado pessoal pseudônimo | Só com consentimento `marketing` (site); CTWA vem de clique em anúncio da própria Meta, base de legítimo interesse, registrada na política de privacidade do site antes do S2 entrar no ar |
| Guardar id para sempre | Poda em 90 dias (S6), que é a janela de importação de conversão do Google Ads |
| Texto do anúncio com dado de terceiro | Só `headline` e `source_id`; `source_url` não é gravado |
| Corpo antigo do site sem `campanha` | Campo opcional; nenhuma quebra de contrato |
| Valor malicioso em UTM | Validação igual à `valorSeguro` do site, no servidor, mais corte de tamanho |
| Migração em tabela grande (`pedidos`) | Só `ADD COLUMN` anulável, sem default, sem índice na primeira fase; índice por `(EmpresaId, Plataforma, CriadoEm)` só se o S5 medir lentidão |

## 6. Aceite

- [ ] Pedido nascido de conversa aberta por anúncio Click-to-WhatsApp de teste aparece no relatório com plataforma `meta` e o título do anúncio.
- [ ] Pedido do site com `gclid` e consentimento `marketing` aparece com plataforma `google` e a campanha da UTM.
- [ ] Com consentimento só de `analytics`, o pedido sai com a UTM e sem id de clique.
- [ ] Pedido sem anúncio continua funcionando e entra como "Sem origem de anúncio".
- [ ] Id de clique com mais de 90 dias fica nulo, e a campanha permanece.
- [ ] O build-check e as suítes citadas em cada fatia passam.

## 7. Decisões do Felipe

| # | Pergunta | Opções | Recomendação |
|---|---|---|---|
| D1 | Clique no botão do WhatsApp do site (sem chat): como ligar à conversa? | (a) Não ligar nesta fase; só chat do site, checkout e CTWA. (b) Código curto no texto ("ref. K7Q2") que o site registra antes de abrir o WhatsApp e o webhook reconhece. | (a). A (b) depende de a cliente não apagar o texto e exige endpoint público novo; medir primeiro quanto do volume vem por esse botão |
| D2 | Regra de atribuição | (a) Último anúncio. (b) Primeiro anúncio. | (a). É o padrão do Google Ads e da Meta para conversão importada |
| D3 | Onde mostrar o relatório | (a) Só endpoint nesta fase, lido por SQL ou pelo console depois. (b) Tela no console já no S5. | (b) se couber em uma tela de tabela simples; senão (a) |

**Decidido pelo Felipe em 2026-11-19:** D1 (a), não ligar o botão do WhatsApp do site nesta fase; D2 (a), último anúncio; D3 (b), tela de tabela simples no console dentro do S5.
