# Sessao Nota Fiscal — mock + operacao visual end-to-end no Web admin

Data: 2026-05-17 22:00
Worktree: C:\easy\EasyStok\.claude\worktrees\awesome-bhabha-1b6721
Branch: dev/awesome-bhabha-1b6721
Identidade Git: felipe.azevedo@gmail.com / michel-az-de
Status final: completo (Fases 1–5 do plano), sem push

## O que foi feito

Implementacao do plano `code-review-debug-analyze-lovely-dove.md` em 5 fases,
fechando o gap entre o backend NF (que ja existia robusto: 6 UseCases, 4
endpoints REST, integracao Focus NFe, audit completo) e a operacao visual
no Web admin. Resultado: a partir do seeder demo, o usuario abre /notas-fiscais,
emite uma NFC-e e cancela — tudo sem certificado A1 real nem token Focus.

### Fase 1 — Mock gateway por empresa
- Novo port `IGatewayFiscalFactory` em `EasyStock.Application/Ports/Output/Fiscal`.
- `GatewayFiscalFactory` em `EasyStock.Infra.Integrations/Fiscal/` resolve
  o adapter (Focus, Mock) por `IGatewayFiscal.Provedor` case-insensitive.
- `MockGatewayFiscal` em `EasyStock.Infra.Integrations/Fiscal/Mock/`:
  - Emite com chave-acesso 44 digitos com DV mod-11 valido (reusa `IGeradorChaveAcesso`).
  - Cancelamento respeita janela 24h (mesma regra Focus).
  - Cache in-memory para `ConsultarStatusAsync` em reconciliacao.
- 4 UseCases (Emitir, Cancelar, Inutilizar, ReprocessarContingencia)
  refatorados: trocam `IGatewayFiscal` por `IGatewayFiscalFactory` e
  resolvem o gateway apos `IConfigFiscalResolver`.
- API + Worker: `AddMockFiscalGateway` + `IGatewayFiscalFactory` registrados.
- Testes (NSubstitute): factory.ObterPara(...) configurado pra retornar o
  gateway substituto.

### Fase 2 — Seeder fiscal Casa da Baba
- Novo `CasaDaBabaSeed.Fiscal.cs` cria `EmpresaConfiguracaoFiscal`
  habilitada com Provedor=mock, Ambiente=Sandbox, IE=ISENTO, Endereco
  (Rua Conselheiro Crispiniano 141, Centro, SP, CEP 01037-001) e CSC
  mock. Idempotente: nao recria se config ja existe.
- Correcao do CNPJ Casa da Baba: `48.735.219/0001-62` → `…-65`
  (DV2 mod-11 estava errado; CNPJ era invalido pelo algoritmo da Receita,
  o que quebrava o `GeradorChaveAcesso.ValidarDvCnpj`).

### Fase 3 — Wizard config fiscal no Web admin (`/configuracao-fiscal`)
- API: 2 endpoints novos em `ConfiguracaoFiscalController`:
  - `POST /api/configuracao-fiscal/dados-emitente` — regime, IE/IM, endereco
  - `POST /api/configuracao-fiscal/provedor` — selecao mock|focus|enotas
  - `GET /` enriquecido com provedor + endereco + IE/IM no payload.
- Web: `ConfiguracaoFiscalController` + `ConfiguracaoFiscalService` +
  `ConfiguracaoFiscalViewModel`.
- `Views/ConfiguracaoFiscal/Index.cshtml` — pagina unica com 6 secoes
  (Emitente, Provedor, Serie/Ambiente, CSC, Certificado, Habilitar).
- Sidebar: novo link "Configuracao Fiscal" (icone engrenagem).
- Banner inline "Modo simulacao ativo" quando Provedor=mock.

### Fase 4 — Emissao manual no Web admin (`/notas-fiscais/emitir`)
- API: 2 endpoints novos em `NotasFiscaisController`:
  - `GET /api/notas-fiscais/pedidos-elegiveis` (status=entregue/pronto sem NFe)
  - `POST /api/notas-fiscais/emitir-de-pedido` (orquestra config + pedido)
- Web: action `Emitir` (GET form + POST submit) + view com tabela de
  pedidos elegiveis + destinatario opcional.
- Botao "+ Nova NFC-e" no header de `/notas-fiscais`.

### Fase 5 — Polish + verificacao
- Empty state `/notas-fiscais`: CTAs para emitir + configurar.
- Botao "Configuracao Fiscal" no header de `/notas-fiscais`.
- Build full verde, 0 erros (13 warnings pre-existentes).
- Tests:
  - Application.Tests Fiscal: 10/10 OK
  - Domain.Tests Fiscal: 46/46 OK
  - ArchitectureTests: 13/16 OK (3 falhas pre-existentes, unrelated)

### Fix incidental — BCrypt regression em Admin
- Sessao anterior (commit 5f7f3e18) introduziu `BCrypt.Net.BCrypt.HashPassword`
  direto em 2 UseCases Admin, quebrando o build (Application nao referencia
  BCrypt) e a arch rule `Application_Nao_Deve_Depender_De_BCrypt`.
- Refatorados pra `IPasswordHasher` (port ja existia em
  `Application.Ports.Output`, impl `BCryptPasswordHasher` em Infra.Async).
- Arch test BCrypt agora passa (1 falha a menos no total).

## O que ficou pendente

- **Upload de cert A1 via UI Web**: tela mostra status mas botao de upload
  ainda nao implementado (mock dispensa; producao usa a API direta hoje).
- **Banner global "Modo simulacao"**: existe apenas inline em
  `/configuracao-fiscal`. Adicionar em `_Layout.cshtml` (precisaria
  fetch + cache do status fiscal por request — adiar).
- **Testes unit do MockGatewayFiscal**: nao foram escritos. Mock e
  exercitado indiretamente pelo seeder + flow E2E. Suite formal cabe
  numa proxima onda.
- **Adapter eNotas**: estrutura no Domain (whitelist provedores) mas
  implementacao real ainda nao existe.
- **Outros 3 arch tests falhando** (pre-existentes, fora de escopo):
  - `Exceptions_De_Domain_Devem_Ficar_No_Domain`
  - `Application_Nao_Deve_Depender_De_EntityFrameworkCore`
    (deliberado segundo comentario no csproj de Application)
  - `RazorViewHygieneTests.Views_NaoDevemConterCoresHexHardcoded`
- **PWA Caixa NFC-e**: explicitamente fora de escopo (decisao do usuario).
  `wwwroot/pwa/caixa/index.html` continua existindo mas sem link.

## Decisoes tomadas

- **Mock por empresa** via `EmpresaConfiguracaoFiscal.ProvedorPreferido`
  ao inves de flag global. Permite tenants reais e mock coexistirem.
- **Wizard "single-page"** com secoes ao inves de fluxo multi-step. Mais
  rapido de operar; estado salvo a cada secao individualmente.
- **Emissao via "select pedido"** ao inves de form manual com itens — o
  domain exige `PedidoId`, e construir um pedido ad-hoc seria scope creep.
- **Web admin escopo total**, **PWA fora**: alinhado com escolha do usuario.
- **Casa da Baba CNPJ corrigido** (1 char): risco baixo, sem outras
  referencias hardcoded no codebase.
- **3 commits separados** ao inves de 1 grande: facilita revisao
  (fix scope vs feat scope) e bisseccao se algo regredir.

## Commits criados

```
4297ab0d feat(web/fiscal): wizard config fiscal + emissao manual NFC-e no admin
ec6aff58 feat(fiscal): mock gateway NFC-e por empresa + seeder demo
19bba051 fix(application): admin UseCases usam IPasswordHasher (preserva arch BCrypt)
```

## Branches criadas/deletadas

Nenhuma — tudo no `dev/awesome-bhabha-1b6721` ja existente.

## Status do working tree

```
M EasyStock.Web/wwwroot/etiqueta/imprimir.js  ← outra sessao, NAO TOCADO
```

## Proxima acao recomendada

1. **Push + abrir PR** (`gh pr create --title "feat(fiscal): operacao NF
   visual end-to-end no Web admin" --base master`). Inclui os 3 commits.
2. **Teste manual** em dev:
   - `dotnet ef database update` + start API
   - Login `admin@casadababa.demo` / `admin123`
   - Sidebar → "Configuracao Fiscal" → ver status "Emissao ativa (modo simulacao)"
   - Sidebar → "Notas Fiscais" → "+ Nova NFC-e" → escolher pedido → emitir
   - Detalhe → Cancelar com motivo 20+ chars → ver historico de eventos
3. **Onda seguinte**: upload de cert A1 via UI Web (multipart), banner
   global "modo simulacao" em `_Layout`, testes unit do MockGatewayFiscal,
   adapter eNotas.

## Referencias

- Plano original: C:\Users\f.michel.de.azevedo\.claude\plans\code-review-debug-analyze-lovely-dove.md
- ADRs relacionadas: 0010 (RLS), 0011 (PT-BR), 0013 (CancellationToken)
- Architecture rule preservada: `Application_Nao_Deve_Depender_De_BCrypt`
- DTO `ConfigFiscalDto.Provedor` (ja existia, agora consumido pelo factory)
