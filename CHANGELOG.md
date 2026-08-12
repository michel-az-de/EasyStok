# Changelog

Todas as mudancas relevantes deste projeto sao documentadas aqui.
Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/).

## [Unreleased]

### Added
- Cadastro de cliente **pessoa juridica** (ADR-0048, item 1 do epico #1013): `TipoPessoa`,
  `NomeFantasia` e `InscricaoEstadual` no `Cliente`, com o formulario do Web alternando entre
  PF e PJ (rotulo, placeholder e campos condicionais). CNPJ ja era aceito antes — o que
  faltava era o discriminador e os dois campos. Nao ha campo `RazaoSocial`: seguindo o
  vocabulario da entidade `Empresa`, o **`Nome` E a razao social** quando o cliente e PJ.
  A busca passa a encontrar PJ tambem pelo nome fantasia. Migration aditiva com default
  `"fisica"` no banco — todo cadastro existente segue pessoa fisica sem UPDATE. (#1018)
- `TenantFeatureFlag` passa a ser lido e escrito em runtime (ADR-0048): repository com filtro
  explicito de `EmpresaId`, endpoint `GET /api/feature-flags` para o tenant logado, e servico
  no BFF Web com cache de 5min por empresa. O portal e o menu lateral escondem modulo que o
  tenant nao tem, com decisao **fail-closed** (Api fora do ar esconde, nao mostra). Item sem
  feature exigida nunca e afetado. Pre-requisito de todo o epico B2B (#1013). (#1016)
- Back-office ganha os endpoints `GET`/`PATCH api/admin/tenants/{id}/features`, que **acendem
  a aba "Features" ja existente** no Admin — ela chamava rotas inexistentes e o `catch`
  engolia a falha, exibindo "Nenhuma feature especial habilitada" como se fosse estado
  normal. O GET devolve o catalogo inteiro com o estado de cada feature, senao um tenant sem
  linha gravada nao teria como ligar o primeiro modulo. (#1016)
- ADR-0048: decisao de **plataforma unica** para o ERP B2B da FMA Informatica — ela entra como
  segundo tenant (`Empresa`) do EasyStok, com modulos novos gated por `TenantFeatureFlag`, em
  vez de fork, greenfield ou segundo backend. Epico com as speks em #1013. (#1014)
- Shell modular no Web: portal de modulos em `/launcher` como home autenticada (tela cheia,
  com pulso do dia, missoes e "Meu dia"), e menu lateral filtrado pelo modulo em que o
  usuario esta. O modulo e DERIVADO DA ROTA — sem querystring, cookie ou sessao —, entao o
  filtro sobrevive a redirect, formulario e paginacao. Nenhuma rota interna muda; o Dashboard
  continua como ancora, visivel dentro de qualquer modulo. Ver ADR-0046. (#1007)
- Missoes do dia no portal: pendencias computadas de dados que o sistema ja tem (pedidos em
  aberto, lotes vencidos, estoque critico, caixa do dia, parcelas vencidas). Sem tabela nova
  e sem migracao; missao sem fonte de dado nao e exibida em vez de aparecer como concluida. (#1007)
- Login em duas etapas com selecao de empresa: usuario com 2+ empresas ativas agora escolhe
  com qual entrar. Ver ADR-0047. (#1007)
- Telemetria OpenTelemetry (traces + metricas + logs) nos 4 hosts (Api/Web/Admin/Worker),
  exportada via OTLP pro otel-collector da VM consolidada -> OpenObserve. Opt-in por
  `OpenTelemetry:OtlpEndpoint` (vazio = desligado; dev/CI intactos). Api ganha Npgsql
  tracing, sink Serilog OTLP e sampler ParentBased (preserva trace distribuido). (#1002)

### Fixed
- `POST /api/empresas/registrar` respondia 500 sob role sem `BYPASSRLS`: registrar empresa e
  cross-tenant por definicao (CRIA o tenant), entao a requisicao anonima nao tem `app.empresa_id`
  e a policy `tenant_isolation` recusava os INSERTs com `42501` em `assinaturas_empresa`. O
  `RegistrarEmpresaUseCase` passa a rodar sob `IRowLevelSecurityBypass.Begin()`, escopado por
  `using`. O escopo cobre o use case inteiro e nao so os INSERTs: `perfis` tambem tem RLS, e sem
  bypass `GetPadroesAsync` voltava vazio — o registro criava um perfil Admin por empresa em vez de
  reusar o padrao, **sem erro nenhum**. Coberto por teste de integracao com role
  `NOSUPERUSER NOBYPASSRLS` mais um controle negativo que prova que a RLS esta viva no ambiente
  (sem ele, um verde poderia significar apenas "a policy nao estava valendo"). (#1024)
- Usuario com 2+ empresas ativas nao conseguia entrar no Web: a Api emite token sem o claim
  `empresaId` nesse caso e o Web tratava como erro terminal ("entre em contato com o suporte").
  O caminho ja existia na Api (`auth/lista-empresas` + `login` aceitando `empresaId`) e nunca
  fora consumido. (#1007)
- `GET /` derrubava a landing publica com `AmbiguousMatchException`: o portal reivindicava o
  mesmo template de rota que o `SiteController`. (#1007)
- Card Financeiro do portal exibia texto de negocio fabricado ("2 contas a vencer hoje",
  variando por tenant) sem consultar nada; passa a usar as parcelas vencidas hoje do
  `financeiro/dashboard`, ou nenhum numero quando a fonte nao responde. (#1007)
- Cards do portal geravam URL malformada (`/estoque?status=vencido?modulo=producao`), que
  quebrava ao mesmo tempo o filtro de vencidos e o modulo. (#1007)

### Changed
- Home autenticada passa a ser `/launcher` em todos os pontos (landing logada, onboarding,
  cardapio, lojas, 404 e primeiro slot do bottom nav mobile). Deep link continua vencendo:
  `returnUrl` valido tem precedencia sobre o portal. (#1007)
- Homolog consolidada na VM unica `hiram-demo-vm` (eastus2, `*.20.98.234.200.sslip.io`), host
  compartilhado com jornada + levante atras de um Caddy central; stack sobe sem caddy proprio
  (`docker-compose.azure.noedge.yml` + `EASYSTOK_COMPOSE_EXTRA` no vm-deploy.sh). VM antiga
  (westus2) descomissionada; observabilidade (Dozzle/Uptime Kuma/OpenObserve) no ar. (#997)

### Security
- `RlsBypassAllowlistTests`: allowlist de quem pode referenciar `IRowLevelSecurityBypass` em
  codigo. O port ja existia e ja tinha consumidores; o que faltava era o portao — injetar uma
  interface e barato demais para uma decisao que desliga a segunda camada de defesa do ADR-0010.
  Agora consumidor novo exige editar a lista, e isso aparece no diff da PR. Mencao em `<see cref>`
  nao conta: falso positivo em guarda de seguranca treina quem mantem o codigo a engordar a
  allowlist ate calar o teste. (#1024)
- Bump `System.Security.Cryptography.Xml` 10.0.7 -> 10.0.10 (5 advisories high / NU1903 que
  quebravam todo PR no CI com `-warnaserror`). (#999)
- Adota o Protocolo Operacional v4.0 (PR-first, issue-driven, auto-merge por tier). Supersede a v3.1
  (master-first). Ver ADR-0043 e CLAUDE.md. (#881)
