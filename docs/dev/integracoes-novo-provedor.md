# Como adicionar um provedor de integração

Issue: #1634 · Épico: #1633 · Atualizado em 2026-10-12

Roteiro prático para ligar um terceiro (pagamento, logística, mensageria, mapa) ao EasyStok.
O plano completo ficará em `docs/plan/integracoes/01-plug-and-play.md` (épico #1633). Este arquivo
descreve só o que já existe no repositório.

## 1. Onde cada peça mora

| Peça | Lugar | Molde para copiar |
|---|---|---|
| Porta (interface que a Application enxerga) | `EasyStock.Application/Ports/Output/<família>/` | `Lookup/`, `Atendimento/`, `Pagamentos/IMercadoPagoClient.cs` |
| Adapter HTTP (fala com o provedor) | `EasyStock.Infra.Integrations/<família>/` | `Rotas/GoogleRotasClient.cs`, `Pagamentos/MercadoPago/MercadoPagoClient.cs` |
| O que lê tenant ou banco | `EasyStock.Infra.Postgre/Services/<família>/` | `Services/Atendimento/RemetenteWhatsAppDoTenant.cs` |
| Credencial por loja (cofre) | `IIntegrationCredentialResolver` em `EasyStock.Application/Ports/Output/Integration/Crypto/` | uso em `RemetenteWhatsAppDoTenant.cs` |
| Resiliência | `AddEasyStockIntegrationResilience` e `IntegrationCategories` em `EasyStock.Infra.Integrations` | `WhatsApp/WhatsAppCloudClient.cs` |
| Entrada de webhook | `EasyStock.Api/Controllers/WebhookGatewayController.cs` (`POST api/webhooks/{provedor}`) | `MercadoPagoSignatureValidator.cs`, `MercadoPagoWebhookProcessor.cs` |
| Fake HTTP de homologação | `scripts/homologacao/` | `fake_mp.py`, `fake_meta.py` |

O adapter em `EasyStock.Infra.Integrations` não lê banco nem tenant. Quando precisa de dado da loja,
recebe por uma porta implementada em `EasyStock.Infra.Postgre/Services/<família>/`.

## 2. O que NÃO fazer

| Não faça | Faça |
|---|---|
| Código novo em `EasyStock.Infra.Async/Pagamentos` | Adapter em `EasyStock.Infra.Integrations/<família>/` |
| Método novo em `IPagamentoGateway` | Porta própria em `EasyStock.Application/Ports/Output/<família>/` |
| Segredo em `appsettings`, log, issue ou teste | `IIntegrationCredentialResolver` (cifrado, por loja e ambiente) |
| Retry em escrita no provedor | Pipeline sem retry, com chave de idempotência do provedor |
| Stub ou simulador ligado em Production | Falhar fechado: em Production só o adapter real |
| Confiar no corpo do webhook | Validar a assinatura e reconsultar o provedor |
| Ligar bypass de RLS para "achar a loja" | Resolver o tenant e testar sob papel sem bypass |
| Handler HTTP redeclarado no arquivo de teste | `HttpFalso` de `EasyStock.TestHelpers` |

## 3. Resiliência

| Tipo de chamada | Pipeline | Exemplo |
|---|---|---|
| Leitura ou consulta idempotente | Categoria de `IntegrationCategories.All` (retry exponencial, disjuntor, timeout de 30 s) | `IntegrationCategories.WhatsApp`, `Payments`, `Logistics` |
| Escrita no provedor (cria cobrança, envia mensagem, pede entrega) | Categoria fora de `All`: só disjuntor e timeout, SEM retry | `IntegrationCategories.WhatsAppEnvio` (#1292) |

Escrita repetida às cegas duplica cobrança, mensagem ou corrida. Se o provedor aceita chave de
idempotência, envie sempre (molde: `X-Idempotency-Key` em `MercadoPagoClient.cs`).

## 4. Webhook

Regra: a ASSINATURA autentica. Identificar a loja não substitui a assinatura. Depois de autenticar,
SEMPRE reconsultar o provedor antes de dar baixa.

| Passo | Peça | Detalhe |
|---|---|---|
| 1. Autenticar | `IWebhookSignatureValidator` | Assinatura inválida ou ausente responde 401. Sem segredo configurado em Production, recusa (molde: `MercadoPagoSignatureValidator.cs`) |
| 2. Deduplicar | `WebhookRecebido` | UNIQUE (`Provedor`, `EventIdExterno`); o segundo evento igual responde 200 sem reprocessar |
| 3. Processar | `IGatewayWebhookProcessor` | Usa o corpo só para saber QUAL recurso mudou; o estado vem da consulta ao provedor |
| 4. Responder | `WebhookGatewayController` | Exceção propagada vira 500 e o provedor reenvia; evento desconhecido responde 200 |

Validador e processor carregam o mesmo `Provedor` (texto), que é o segmento da rota.
Webhooks da Meta têm controllers próprios em `EasyStock.Api/Controllers/Webhooks/` e validam com
`AssinaturaWebhookMeta.cs`.

## 5. Testes

| O que provar | Com o quê | Onde |
|---|---|---|
| Método, URL, cabeçalhos e corpo enviados | `HttpFalso` (`EasyStock.TestHelpers/Http/HttpFalso.cs`) | `EasyStock.Infra.Integrations.UnitTests/<família>/` |
| Erro HTTP, timeout, queda de rede | `HttpFalso`: `Responde(500)`, `Lanca(...)`, `Demora(...)` | idem |
| Retry só em leitura, nunca em escrita | `HttpFalso` com sequência (`Responde(500).Responde(200)`) e contagem de `Chamadas` | idem |
| Webhook assinado passa pelo validador real | `AssinaturaMercadoPago`, `AssinaturaMeta` (`EasyStock.TestHelpers/Webhooks/`) | `EasyStock.Api.UnitTests` |
| Adulteração e replay são recusados | Mesmas fábricas, trocando corpo, segredo ou instante | idem |
| Leitura por loja respeita RLS | `RlsClientConnectionString` da `PostgreSqlDatabaseFixture` (papel sem bypass) | `EasyStock.Infra.Postgre.IntegrationTests/` (molde: `Atendimento/RemetenteWhatsAppDoTenantIntegrationTests.cs`) |
| Bypass de RLS continua restrito | `EasyStock.ArchitectureTests/RlsBypassAllowlistTests.cs` | Editar a allowlist aparece no diff e exige justificativa |

Exemplo mínimo com o `HttpFalso`:

```csharp
var falso = new HttpFalso();
falso.Quando(HttpMethod.Get, "/v1/cotacoes/7")
    .Responde(HttpStatusCode.InternalServerError)
    .Responde(HttpStatusCode.OK, """{"id":7,"valor":12.5}""");
var adapter = new MeuProvedorClient(falso.CriarClient("https://provedor.test/"), ...);

var cotacao = await adapter.ConsultarAsync("7");

falso.Chamadas.Should().HaveCount(2);
falso.UltimaChamada!.Cabecalho("Authorization").Should().Be("Bearer token-de-teste");
```

Rota não registrada lança `InvalidOperationException` com método, URL e as rotas conhecidas. Não
existe resposta padrão silenciosa.

## 6. Checklist

- [ ] Porta em `EasyStock.Application/Ports/Output/<família>/`, sem tipo do provedor na assinatura
- [ ] Adapter HTTP em `EasyStock.Infra.Integrations/<família>/`, sem acesso a banco
- [ ] Leitura de tenant ou banco em `EasyStock.Infra.Postgre/Services/<família>/`
- [ ] Credencial pelo `IIntegrationCredentialResolver`; nenhum segredo em arquivo, log ou teste
- [ ] Leitura no pipeline com retry; escrita em pipeline SEM retry e com chave de idempotência
- [ ] Webhook: validador de assinatura, dedupe em `WebhookRecebido`, reconsulta ao provedor
- [ ] Sem segredo de webhook em Production, o validador recusa
- [ ] Stub ou simulador não sobe em Production
- [ ] Testes do adapter com `HttpFalso`: caminho feliz, erro HTTP, timeout, corpo inválido
- [ ] Testes do webhook com as fábricas de assinatura: aceita, adulterado recusa, replay recusa
- [ ] Teste de integração sob `RlsClientConnectionString` para o que lê por loja
- [ ] `RlsBypassAllowlistTests` intacto, ou alteração justificada na PR
- [ ] Fake HTTP do provedor em `scripts/homologacao/` para a bateria local
- [ ] Nada novo em `EasyStock.Infra.Async/Pagamentos` nem em `IPagamentoGateway`
- [ ] Fragmento em `changelog.d/<issue>.md`
