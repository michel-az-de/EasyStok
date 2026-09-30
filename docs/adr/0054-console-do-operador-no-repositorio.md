# ADR-0054 — O protótipo vira o console do operador, dentro deste repositório

- Status: Aceito
- Data: 2026-09-29
- Supersede: a frase "front do operador em outra tecnologia e outro repositório" do ADR-0049, mantida
  pelo ADR-0050, e o parágrafo de escopo de `docs/plan/atendimento-whatsapp/README.md`. O resto dos
  ADR-0049/0050/0051 continua valendo.
- Relacionados: ADR-0046 (shell modular), ADR-0051; issue #1120; origem
  `michel-az-de/casa-da-baba-atendimento`, `prototipo-omni/` no `main` a9831ab

## Contexto

O console do operador existe como protótipo React (`prototipo-omni`, 13 rodadas com a Thatiane), com
dados simulados. O backend dele está sendo escrito aqui, spec por spec (S01 a S48). Com tela num
repositório e API em outro, nenhuma PR entrega a funcionalidade inteira e o contrato só é testado
quando alguém junta os dois à mão.

## Decisão

1. **O protótipo é o console oficial**, na pasta `EasyStock.Console/` deste repositório. React 19 +
   Vite, JavaScript, como está. Migrar para TypeScript não é pré-requisito.
2. **A regra de negócio mora na API.** O `src/dominio` do protótipo vira lógica de tela: o que já
   existe no backend deixa de ser calculado no navegador conforme cada módulo é ligado.
3. **Ligar módulo a módulo.** Cada módulo troca o repositório simulado (`src/infra/repositorio*.js`)
   pela chamada à API real. A ordem e o estado ficam na matriz de
   `docs/plan/atendimento-whatsapp/10-console.md`. Uma tela só é ligada quando a spec backend dela
   está no master.
4. **Estrangulamento das telas antigas.** O EasyStock.Web e a PWA de `Api/wwwroot/pwa` continuam no
   ar. Cada módulo que o console assume em paridade sai do legado na PR seguinte (P05 do plano de
   poda já exige isso para a PWA).
5. **Uma PR, uma funcionalidade.** A partir daqui a PR de uma funcionalidade pode levar endpoint e
   tela juntos. As specs backend já escritas continuam valendo como estão.
6. **CI separada.** `.github/workflows/console.yml` roda `npm run qualidade` (lint, fronteira de
   camadas e build) só quando `EasyStock.Console/**` muda. O gate .NET não muda.

## Alternativas descartadas

- **Manter o protótipo em outro repositório.** Duas PRs por funcionalidade e contrato sem teste
  conjunto; foi o motivo desta decisão.
- **Reescrever o console em Blazor ou nas Views do EasyStock.Web.** Joga fora 13 rodadas validadas
  com a dona e troca a tecnologia que já funciona por uma que o operador nunca viu.
- **Trazer com o histórico (`git subtree`).** O repositório de origem tem mais de 100 branches e a
  pasta de estudos; o histórico continua lá, e o commit de origem fica registrado aqui.

## Consequências

- O que veio junto e não é tela fica isolado e marcado: `servidor/agente.mjs` (agente local de
  demonstração; o agente real é o do S06), `Dockerfile.api-falsa` e `dados/` (massa sintética).
  Saem quando o módulo correspondente estiver ligado.
- Os geradores de uso único (`ferramentas/*.py`) e as saídas de avaliação não vieram; estão no
  repositório de origem.
- Deploy do console (`app.easystok.online`) e login JWT com tenant entram na F01, com issue própria.
