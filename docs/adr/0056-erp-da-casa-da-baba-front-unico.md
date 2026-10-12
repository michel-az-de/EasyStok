# ADR-0056 — EasyStok é o ERP da Casa da Baba: tenant único, front único e sala de módulos

- Status: Proposto
- Data: 2026-10-01
- Emendado por ADR-0059 (2026-10-08): o `EasyStock.Web` sai inteiro (supersede o item 7 na parte do
  bastidor), a PWA de produção e cadastro continua como backup offline e conflito vai para um
  módulo de merge.
- Supersede: ADR-0048 (FMA como 2º tenant) por inteiro; o item 4 do ADR-0054 (estrangulamento
  total do EasyStock.Web).
- Emenda: ADR-0054 (o console deixa de ser só o atendimento e vira o front único da operação);
  ADR-0055 (as fatias `Mx.y` do plano `docs/plan/erp-casa-da-baba/` contam como spec de plano
  aprovado).
- Relacionados: ADR-0010 (RLS), ADR-0046 (shell modular do Web), ADR-0047 (login multi-empresa),
  ADR-0050/0051 (canais), issue #1316, plano `docs/plan/erp-casa-da-baba/`

## Contexto

O EasyStok deixou de ser produto de mercado em 2026-08 e, desde 30/09, é a ferramenta da Casa da
Baba. Em 01/10 o Felipe desenhou o sistema como um ERP de sete módulos (Cardápio, Produção,
Atendimento, Cozinha, Caixa, Campanhas, Configurações) acessados por uma sala de módulos, e pediu
que tudo tenha a cara da Casa da Baba e opere do jeito do protótipo de atendimento.

Fatos medidos no master `0a70eb56` (estudo da issue #1316):

- O backend já cobre a maior parte do desenho: Atendimento, Cozinha e Caixa vivos de ponta a
  ponta; Cardápio vivo sem combos; Produção, Campanhas e Configurações parciais.
- Há quatro fronts vivos: `EasyStock.Web` (back-office, em produção), a PWA do caixa
  (`Api/wwwroot/pwa`, uso diário), o storefront `casadababa.com` (outro repositório) e o
  `EasyStock.Console` (ainda fora do ar; go-live em F16/F13).
- O multi-tenant ocupa 105 de 156 tabelas (`EmpresaId`), um filtro global do EF e 20 migrations
  de RLS. Do ADR-0048 só saiu o Cliente PJ.
- `Perfil`/`PerfilPermissao`/`UsuarioPerfil` existem, mas o enum `Permissao` ainda carrega
  helpdesk e faturas removidos; a autorização real é por nível (`Operador`, `Gerente`, `Admin`).

## Decisão

1. **Tenant único.** A Casa da Baba é a única empresa. A FMA sai: o ADR-0048 é superado e o
   resíduo B2B (Cliente PJ, flags `modulo.comercial`/`modulo.crm`) e SaaS (`Plano`,
   `AssinaturaEmpresa`, `CobrancaAssinatura`, `Fatura*`, `Cupom` de plano, criar e listar empresa no
   `AdminTenantsController`, `CriarTenantPorAdmin`) entram na poda. Ligar módulos e canais e vincular o
   número da Meta (`AdminTenantsController`) ficam e migram para o M7.
2. **A infraestrutura de tenant fica.** `EmpresaId`, o filtro do EF e o RLS continuam, com o
   tenant fixo. Removê-los custaria migrations destrutivas em 105 tabelas e ~61 testes de
   isolamento para ganho nulo; o RLS segue como rede contra SQL cru (ADR-0010). Tabela nova
   continua com o bloco `DO $rls$`.
3. **Front único = `EasyStock.Console` evoluído.** O fluxo é **login → sala de módulos → módulo
   com menu próprio**. Módulos: M1 Cardápio, M2 Produção, M3 Atendimento, M4 Cozinha, M5 Caixa,
   M6 Campanhas, M7 Configurações e **M8 Entregas** (não estava no rascunho; é operação diária e já
   existe no console).
4. **O atendimento continua sendo o cockpit.** A diretriz D5 da Thati ("uma tela só") vale dentro
   da operação: o perfil de atendimento entra direto no balcão do M3, e Cozinha e Entregas abrem
   direto na tela de dispositivo. A sala de módulos é o mapa da gestão, não um passo a mais no
   pedido.
5. **Permissão por perfil × módulo.** O card da sala aparece liberado ou bloqueado conforme o
   perfil. Reaproveita `Perfil`/`PerfilPermissao`; a API aplica a mesma regra por módulo (a tela
   só esconde, quem nega é a API).
6. **Roupa da Casa da Baba.** O console adota o design-system-v1 da marca (hoje marcado como proposta
   visual aguardando aceite; o aceite do Felipe vem pelo mockup da M0.1): Lora nos títulos,
   Nunito Sans no texto, Cacau, Caramelo, Creme, Trigo, com uma camada de tela própria. A paleta
   tomate do protótipo sai.
7. **Escopo do refazer:** console (todos os módulos), PWA do caixa (sai quando o M5 chegar à
   paridade), site `casadababa.com` e impressos. **O `EasyStock.Web` fica** para o bastidor
   (estoque, compras, financeiro, etiquetas); ele deixa de ser estrangulado por inteiro, e o
   item 4 do ADR-0054 passa a valer só para a PWA e para as telas que o console assumir.
8. **O go-live do atendimento não espera o redesenho.** F16 e F13 seguem; o shell novo envolve o
   console depois, sem mudar o que a Thati já homologou.

## Alternativas descartadas

- **Front novo do zero.** Refaz a integração com a API que o console já tem (F01–F05) e joga fora
  13 rodadas homologadas com a dona.
- **Remover `EmpresaId` e RLS.** Ver item 2.
- **Manter a FMA como 2º tenant.** Contraria o "tenant único" e mantém flags e resíduo sem uso.
- **Aposentar o EasyStock.Web inteiro.** O Felipe escolheu manter o bastidor nele; reescrever
  estoque, compras e financeiro no console agora atrasaria a operação.

## Consequências

- O ADR-0048 passa a `Superseded por ADR-0056`; o épico #1013 fecha sem as fatias restantes.
- O console ganha shell, login real, sala de módulos e tema da marca antes de qualquer módulo novo.
- O enum `Permissao` é limpo e ganha as permissões de módulo; isso mexe em autorização e é tier
  alto.
- O plano `docs/plan/erp-casa-da-baba/` vira a fonte das fatias `Mx.y`; o plano do atendimento
  continua dono de S01–S53 e F01–F18.
