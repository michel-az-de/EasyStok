# Relacionamento do cliente e Novo pedido com o cardápio (#3237)

- Data: 2026-11-23
- Base medida: `origin/master` `1aa58acbe` (inclui a #3234, PR #3236)
- Issue mãe: #3237. Esta spec é o S0, issue #3238.
- Relacionadas: #2921 (Novo pedido na tela, spec `novo-pedido-na-tela-2921.md`, emendada aqui), #3158 (momento de abrir a conversa, aberta), #3234 (crítica do Atendimento, aberta; só C2 e C3 entraram, pela PR #3236), #3193 (vocabulário "Sem pedido"), #3076 e #2777 (Cadastros e regra de compra).
- Status: plano aprovado pelo Felipe em 23/11/2026 às 18:14, com as palavras "Aprovo o plano"; antes disso, a banca independente aprovou com ajustes em 23/11/2026 (seção 4). O Felipe deu GO no S0 (esta spec) em 23/11/2026 e validou o protótipo clicável no mesmo dia, com dados fictícios. O protótipo é um artifact privado e não é citado por link.
- Tier desta PR: baixo (docs, ADR-0055). As fatias têm tier próprio na seção 5.
- Atlas: busca por EasyStok voltou vazia (lacuna declarada). Fonte usada: repositório, issues e leitura somente de produção (contagens e índices).

## 1. Problema

Duas dores, um objetivo: a equipe ENXERGA e AGE sobre a relação de cada pessoa com a loja sem abrir a Ficha, e monta pedido do MESMO jeito em qualquer tela.

A. Em Cadastros, "Leads e clientes" é uma lista de cadastro, não de relacionamento. O grid tem 4 colunas (Nome com ícone genérico e "N pedidos", Contato, Situação, Ações; `EasyStock.Console/src/features/gestao/cadastros/PainelCadastros.jsx`, linhas 218 a 235). Não mostra a foto, o gasto, as compras, o ticket médio, a última compra, a reclamação, as notas nem a próxima ação. Tudo isso já existe, só que por cliente (Ficha e Atendimento), não em lote.

B. A janela Novo pedido monta os itens com uma lista de texto e "+" (`EasyStock.Console/src/app/NovoPedidoJanela.jsx`, passo `itens`, linhas 218 a 234). A tela de Atendimento usa outro cardápio, com foto, grupos por categoria e porções como botões com preço (`EasyStock.Console/src/features/cardapio/PainelCardapio.jsx`). Quem usa as duas telas vê dois jeitos de fazer a mesma coisa.

As imagens de referência (Bitrix24) servem de IDEIA de colunas ricas, barra de fase, valor e filtros com contagem. O visual segue o do EasyStok.

## 2. Foco

Console no navegador em tela larga. Prova em 1920 e 1440. No celular (390) a regra é só não quebrar: sem rolagem horizontal da página e sem perder ação. Não se desenha versão móvel nova.

## 3. Abordagem

O que já existe (medido em 23/11/2026 no worktree):

| Peça | Onde | Estado |
|---|---|---|
| Lista na API | `ClientesController.GetAll` (policy `Operador`), `ListarClientesUseCase`, `ClienteRepository.GetByEmpresaAsync` (`EasyStock.Infra.Postgre/Repositories/ClienteRepository.cs`, linhas 36 a 107) | devolve `OrderCount` e `LastOrderAt` por cliente e já devolve `FotoUrl` (`ClienteResult.cs`). NÃO devolve gasto, ticket, reclamação nem notas. Aceita o filtro `relacionamento` = `lead`, `cliente` ou `recompra` |
| Regra de compra | `EasyStock.Domain/Sales/RegraDeCompra.cs` (#2777) | cancelado, rascunho e aguardando pagamento não contam |
| Recompra | `EasyStock.Domain/Sales/RegraDeRecompra.cs` | 2 compras ou mais, nenhuma nos últimos 30 dias, sem a tag `recompra_convidada` nos 30 dias |
| Foto do cliente | `Cliente.FotoChave`, `ClientesFotoController` (`api/clientes/{clienteId}/foto`), `EasyStock.Console/src/aplicacao/useFotoCliente.js` | o hook carrega a imagem com JWT; o `no-store` é cabeçalho da API (`ClientesFotoController.cs`, linha 34); a lista já traz a URL, o grid é que não desenha |
| Dossiê | `ClientesCrmController` (`Operador`) | existe por cliente (Ficha e Atendimento), não em lote |
| Reclamação | entidade `Ocorrencia` (`EasyStock.Domain/Entities/Atendimento/`), `OcorrenciasController` | `Operador`. Produção: 0 registros |
| Notas e tags | `RelacionamentoCliente.jsx` (editor do cadastro) e `ClientesCrmController` | por cliente |
| Assistente | `AtendimentoAssistenteController` (`api/atendimento/assistente`, rotas `cadastro` e `ficha`) | `ConversaId` (opcional na pergunta, obrigatório em `cadastro` e `ficha`), nunca cliente. Não existe nada na lista de clientes |
| Interesse de item | `ClienteInteressesController` | policy `Admin` |
| Cardápio do Atendimento | `PainelCardapio.jsx`: `FotoDoItem` (linha 57), `ItemEscolher` (124), `PorCategoria` (288) | completo, mas interno ao painel |
| Novo pedido | `NovoPedidoJanela.jsx`, domínio `dominio/cardapio.js` e `dominio/filtroDoCardapio.js` | lista própria de texto. `porcaoDoToque` é comum; há dois `filtrarCardapio` homônimos (`dominio/vitrineCardapio.js`, linha 47, usado pelo Atendimento; `dominio/filtroDoCardapio.js`, linha 38, usado por esta janela e pela aba de gestão `AbaCardapioApi.jsx`). Decisão no S5 |
| Volume em produção | 37 clientes, 80 pedidos, 5 notas; `IX_pedidos_ClienteId` existe (medido em 23/11/2026) | v1 sem migração |

O que muda, em uma frase por fatia:

- F: o grid desenha a foto que a lista já devolve.
- S1: a API da lista passa a devolver métricas, estágio e risco por linha, calculados no servidor.
- S2: o grid ganha a linha nova (foto, métricas, estágio, sinais, ação).
- S3: faixa de indicadores da carteira no topo, e tocar num indicador filtra a lista.
- S4a: "Próxima ação" por regra, no servidor.
- S5: extrair o cardápio de escolha do Atendimento para um componente compartilhado, sem mudar o Atendimento.
- S6: o Novo pedido usa o cardápio compartilhado.

## 4. Decisões

Ajustes da banca (23/11/2026) que valem como decisão:

1. Estágios, nesta ordem de vocabulário: "Sem pedido" (0 compras, termo da #3193, não "Lead") > 1ª compra (1) > Recorrente (2 a 4) > Fiel (5 ou mais).
2. O estágio e o "Em risco" são calculados NA API, com os limiares e filtros da `RegraDeRecompra` (e a `RegraDeCompra` para o que é compra). O Console só desenha. Calcular nos dois lados divergiria.
3. Toda subconsulta de S1 filtra `EmpresaId`. Há teste de dois inquilinos: o cliente da empresa B nunca soma em A, em nenhuma métrica.
4. Interesse de item fica FORA da v1: o endpoint é `Admin` e a lista é `Operador`. Ampliar a permissão seria decisão de segurança, tier alto, fora deste escopo.
5. A IA só propõe e a equipe envia. Nada sai sozinho.
6. Sem migração. O índice `IX_pedidos_ClienteId` existe no modelo e em produção (medido em 23/11/2026), então o desempenho da lista não é risco da v1. Se a base crescer e o EXPLAIN piorar, um índice novo vira issue própria, tier alto.
7. A foto vira fatia própria (F), só no Console: a API já devolve `FotoUrl`.
8. O S4b (mensagem da IA a partir da lista) fica condicionado (seção 5).

Outras decisões:

- "Compra" é a `RegraDeCompra` (sem cancelado, rascunho e aguardando pagamento), a mesma da Ficha, do filtro lead e cliente e da ordenação (#2777). Gasto, ticket médio e última compra usam o mesmo filtro.
- O S1 devolve um record próprio da lista, com todos os campos que o `GET api/clientes` devolve hoje (`ClienteResult`) mais os novos, para a ordem de deploy da seção 8 seguir segura. Não se engorda o `ClienteResult`, usado em outros fluxos (criar, atualizar, buscar).
- "Em risco" é exatamente o filtro `relacionamento=recompra` da `RegraDeRecompra` (constantes em `RegraDeRecompra.cs`, linhas 10 a 15; predicado em `ClienteRepository.cs`, linhas 49 a 57): cadastro ativo e não bloqueado, 2 compras ou mais, nenhuma compra nos últimos 30 dias e sem a tag `recompra_convidada` criada nos últimos 30 dias. O predicado vira uma expressão só, usada pelo filtro e pelo campo `emRisco`. Efeito aceito: o clique em "Chamar no WhatsApp" grava essa tag (`aplicacao/conviteDeRecompra.js`, linha 12; `dominio/recompra.js`, linha 5), então quem foi convidada sai de "em risco" por 30 dias.
- Agregados da carteira são calculados NA API sobre a carteira inteira da empresa (todos os clientes, `RegraDeCompra`), nunca somando a página da lista (20 por página no Console, `dominio/cadastros.js`, linha 170; até 200 na API, `ListarClientesUseCase.cs`, linha 31). "Ativo" é o cadastro `Ativo` e não `Bloqueado`.
- O parâmetro `relacionamento` da lista (`lead`, `cliente`, `recompra`) já tem significado. Os novos filtros por estágio e risco usam parâmetro novo, sem reaproveitar esse.
- O nome `RelacionamentoCliente.jsx` já existe (notas e tags do editor). O módulo novo de domínio é `dominio/relacionamento.js` (puro: barras, rótulos), com nome distinto para não confundir.
- O Atendimento não muda visualmente com S5. A prova de regressão é obrigatória.
- Não se mexe no momento de abrir a conversa do Novo pedido (#3158), nem em tier ou critérios da #2921.
- Título da tela (`TITULOS_CADASTROS.clientes` = "Leads e clientes", `EasyStock.Console/src/dominio/cadastros.js`, linha 258): continua "Leads e clientes", por decisão do Felipe em 23/11/2026 (seção 11).

## 5. Fatias

| id | O que entrega | Arquivos prováveis | Aceite verificável | Tier | Depende de |
|---|---|---|---|---|---|
| F | Foto (ou iniciais) em cada linha do grid de clientes, carga preguiçosa | `PainelCadastros.jsx`, `cadastros.module.css`; reaproveita `componentes/Avatar` e `aplicacao/useFotoCliente.js` | Red: prova mostra o grid sem foto. Green: cliente com `fotoUrl` mostra a imagem; sem foto mostra iniciais; foto só carrega para a linha visível; sem mudança de API; prova de navegador em 1920 e 1440 | baixo | nenhuma |
| S1 | A lista devolve por linha: gasto total, compras, ticket médio, primeira e última compra, ocorrências abertas e total, notas (total e última curta), até 3 tags, estágio e em risco; no envelope da lista, `gastoTotalDaCarteira` (denominador do S2). Ordens "maior gasto" e "ticket médio". Record próprio | `EasyStock.Application/UseCases/ListarClientes/ListarClientesUseCase.cs`, `IClienteRepository.cs`, `EasyStock.Infra.Postgre/Repositories/ClienteRepository.cs`, `EasyStock.Api/Controllers/ClientesController.cs` | Red: teste de repositório e de API falham sem os campos. Green: valores batem com a `RegraDeCompra`; estágio 0, 1, 2 a 4 e 5 ou mais; `emRisco` verdadeiro se e só se o cliente aparece com `relacionamento=recompra`; `gastoTotalDaCarteira` é a soma do gasto de todos os clientes da empresa, igual em qualquer página e filtro; o record traz todos os campos do `ClienteResult` (teste de contrato); teste com dois inquilinos prova que toda subconsulta filtra `EmpresaId`; sem migração; página de 200 linhas sem consulta por linha (N+1) | baixo | F (ordem de entrega, não técnica) |
| S2 | Linha nova do grid: foto, nome, "N compras", gasto, ticket, última compra (há N dias), cliente desde, barra de estágio com alerta Em risco, barra de gasto em % da carteira (gasto do cliente dividido por `gastoTotalDaCarteira`, 0 se o total for 0), sinal de reclamação e notas, tags | `PainelCadastros.jsx`, `cadastros.module.css`, `EasyStock.Console/src/dominio/cadastros.js`, novo `EasyStock.Console/src/dominio/relacionamento.js`, tradução em `EasyStock.Console/src/infra/api` | Red antes: provas de domínio de `relacionamento.js` falham. Green: o Console desenha o estágio que a API mandou (sem recalcular); prova de navegador 1920 e 1440 sem rolagem horizontal e sem sobreposição; 390 não quebra; estados vazio, carregando e erro; as ações Novo pedido, Conversa e Ficha, o motivo de loja desligada (#3193, `PainelCadastros.jsx`, linhas 208 e 230) e o pedido de celular seguem iguais (a `prova-3193-consistencia-navegador.mjs` cobre o motivo de loja desligada e o termo "Sem pedido"; o convite "Chamar no WhatsApp" e o pedido de celular não têm prova nela) | baixo | S1 |
| S3 | Faixa de indicadores no topo: ativos, % recorrentes, ticket médio, em risco, reclamações abertas; tocar num indicador filtra a lista | `ListarClientesUseCase.cs` e controlador (endpoint de indicadores, policy `Operador` como o `GetAll`, `ClientesController.cs`, linha 43, e `TryResolveEmpresaId`, `EasyStockControllerBase.cs`, linha 100), `EasyStock.Api.UnitTests/Authorization/` (teste novo), `PainelCadastros.jsx`, `cadastros.module.css` | Red: teste do endpoint falha. Green: sobre a carteira inteira, `Ativos` = clientes ativos e não bloqueados; `% recorrentes` = ativos com 2 compras ou mais dividido por ativos com 1 compra ou mais; `Ticket médio` = gasto dos ativos dividido pelas compras dos ativos (razão das somas, não média dos tickets); `Em risco` = clientes da `RegraDeRecompra`; `Reclamações abertas` = `Ocorrencia.EstaAberta` da empresa. Teste com carteira maior que uma página compara cada indicador com o cálculo sobre todas as páginas do mesmo filtro; policy `Operador`, `TryResolveEmpresaId` e teste em `EasyStock.Api.UnitTests/Authorization/` no padrão de `ClientesNotasControllerTests.cs`, com o caso "Operador da empresa A com `?empresaId=B` recebe 403"; filtra `EmpresaId` (teste de dois inquilinos); indicador filtra a lista e o filtro aparece e se limpa; prova 1920 e 1440 | baixo | S2 |
| S4a | Coluna "Próxima ação" por regra no servidor: recompra devida, reclamação aberta, cadastro incompleto, sem pedido parado. Ações rápidas: Novo pedido, Conversa, Ficha | `ListarClientesUseCase.cs`, `PainelCadastros.jsx`; reaproveita `useNovoPedidoDoCliente` | Red: teste de unidade da regra falha. Green: cada regra da tabela abaixo produz a ação e o motivo esperados, na prioridade da tabela; as ações existentes da linha seguem iguais (S2); a ação "Novo pedido" abre a janela da #2921 sem trocar a hash; sem IA | baixo | S3 |
| S5 | Cardápio de escolha compartilhado: extrair `ItemEscolher`, `PorCategoria`, `FotoDoItem` e a busca de `PainelCardapio.jsx` para `EasyStock.Console/src/features/cardapio/EscolhaDoCardapio.jsx` | `PainelCardapio.jsx`, novo `EscolhaDoCardapio.jsx`, `cardapio.module.css` | Red: prova de regressão do Atendimento capturada ANTES da extração. Green: o Atendimento fica igual (mesma prova, mesmos prints em 1920 e 1440); sem mudança de comportamento. A busca do compartilhado é a de `vitrineCardapio.js` (cada palavra precisa aparecer, "lasanha 600"); `filtroDoCardapio.js` fica, porque `AbaCardapioApi.jsx` o usa. O PR cita as mudanças do Atendimento já mergeadas (#3234) | baixo | nenhuma (frente independente) |
| S6 | Novo pedido com o cardápio do Atendimento: coluna Itens usa `EscolhaDoCardapio`; coluna Pedido com miniatura e contador; cabeçalho com foto e "costuma pedir" | `EasyStock.Console/src/app/NovoPedidoJanela.jsx`, `novoPedidoJanela.module.css` | Red: prova nova falha com a lista de texto. Green: foto, grupos por categoria e porções como botões com preço; "N no pedido" com "-"; busca por prato, categoria ou porção; a busca do Novo pedido passa a aceitar várias palavras (prova com "lasanha 600") e `NovoPedidoJanela.jsx` deixa de importar `filtroDoCardapio`; a observação por item (campo "Observação de", `NovoPedidoJanela.jsx`, linha 298) é preservada: `prova-2921-janela-do-pedido.mjs` (linha 71) já cobre o envio no registro e `prova-2921-rascunho.mjs` (linha 35) o rascunho, e a prova nova acrescenta digitar a observação na coluna Pedido; `prova-3076-cadastro-pedido-navegador.mjs` (linha 60), que clica em "Somar Lasanha", muda só o seletor se o "+" mudar; o motor da #2921 (comanda, cotação, registro, resumo) não muda; não muda o momento de abrir a conversa (#3158); provas da #2921 seguem verdes; prova 1920 e 1440 | alto (spec 2921, seção 9) | S5 |
| S4b | "Sugerir com a IA" a partir da lista, com mensagem proposta e envio pela equipe | A definir | CONDICIONADA: só entra com as três coisas abaixo decididas em spec própria | alto | S4a e as três condições |

Regras do S4a (a primeira que valer ganha; a ordem é a da entrega original):

| Prioridade | Condição | Próxima ação |
|---|---|---|
| 1 | Em risco (`RegraDeRecompra`, seção 4) | Chamar no WhatsApp (já existe na aba Recompra); sem telefone, Ficha |
| 2 | Alguma `Ocorrencia` aberta do cliente | Conversa |
| 3 | Cadastro incompleto: sem telefone (o caso que hoje abre o pedido de celular) | Ficha |
| 4 | Sem pedido parado: 0 compras e cadastro criado há mais de 30 dias (`RegraDeRecompra.DiasSemCompra`) | Novo pedido |

Condições do S4b (todas):

1. Contexto por cliente na API. Hoje o Assistente recebe `ConversaId`, não cliente (`AtendimentoAssistenteController`). Nenhuma rota recebe cliente.
2. Canal fora da janela de 24 h: `wa.me` (a equipe envia pelo próprio aparelho) ou modelo aprovado no WhatsApp. Sem isso a mensagem para cliente inativa não sai.
3. Teto de uso (custo e latência controlados, ação sob demanda, uma cliente por vez).

Tier: com o plano aprovado pelo Felipe em 23/11/2026 às 18:14 ("Aprovo o plano"), F e S1 a S5 são tier baixo pela ADR-0055 (spec de plano aprovado em `docs/plan/**`, merge no verde). F é fatia desta spec, sem número S, e segue o mesmo regime das fatias S. S6 segue em tier alto, por herdar a spec 2921 (feat na janela Novo pedido): a PR fica aberta até a label `aprovado`, nas condições da ADR-0070.

## 6. Ordem e paralelismo

Frente A: F > S1 > S2 > S3 > S4a. Frente B: S5 > S6. As duas frentes são independentes e podem correr em paralelo. S4b só depois da decisão de canal. Cada fatia é uma issue filha e uma PR, com `closes #N` e fragmento em `changelog.d/<issue>.md`. Rebase de cada PR antes de abrir, por causa das specs vizinhas (#2921, #3158, #3076).

Estimativa (não medida): F 1 h, S1 2 h, S2 3 h, S3 2 h, S4a 2 h, S5 2 h, S6 3 h, cerca de 15 h de execução, mais CI e deploy por fatia. S4b: 3 h, só depois da decisão.

## 7. Prova

- Teste Red antes do Green em cada fatia. Bug de regra de API: teste em `EasyStock.Application.Tests` e, para consulta, em `EasyStock.Infra.Postgre.IntegrationTests` (Postgres real), com dois inquilinos.
- Console: `npm run qualidade` (em `EasyStock.Console`) e `powershell -File scripts/poka-yoke/build-check.ps1`.
- Prova de navegador em `EasyStock.Console/ferramentas/prova-<issue>-*.mjs`, no Console real (build do worktree, API local semeada), em 1920 e 1440. O celular (390) só confere que não quebra.
- O S5 exige a prova de regressão do Atendimento capturada antes e comparada depois.

## 8. Deploy

API antes do Console: `scripts/deploy/vps-deploy.sh`, depois `scripts/deploy/console-deploy.sh`. Os campos novos da lista são aditivos: um Console antigo os ignora, então a ordem nunca quebra a tela. Validação em produção SÓ de leitura: a lista traz foto, métricas e estágio, a tela mostra os números sem criar pedido, nota nem mensagem.

## 9. Riscos e rollback

| Fatia | Risco | Mitigação | Rollback |
|---|---|---|---|
| F | Fotos pesando a lista | Carga preguiçosa, só a linha visível baixa | Reverter a PR. Sem dado a desfazer |
| S1 | Lista lenta com base maior; vazamento entre inquilinos nas subconsultas | `IX_pedidos_ClienteId` existe; EXPLAIN no teste de integração; filtro `EmpresaId` com teste de dois inquilinos | Reverter a PR. Campos aditivos, sem migração |
| S2 | Tela wide subaproveitada ou poluída; estágio divergente | Colunas desenhadas para 1920; prova em 1920 e 1440; o Console não recalcula | Reverter a PR. O grid volta ao de 4 colunas |
| S3 | Número do indicador diferente da lista | Teste compara indicador com soma das linhas | Reverter a PR |
| S4a | Ação errada sugerida | Regras por teste, prioridade documentada, sem IA | Reverter a PR |
| S5 | Regressão silenciosa no Atendimento | Prova antes e depois com prints; colisão com #3234: a issue segue aberta (da crítica só entraram C2 e C3, pela PR #3236; C1, R2, G1 e G2 ainda vêm), então trazer o master e refazer a prova antes e depois na hora do merge | Reverter a PR. O Atendimento volta ao componente interno |
| S6 | Colisão com #2921, #3158 e #3076; mensagem acidental | Motor intocado; provas da #2921 verdes; sem campo de texto na janela | Reverter a PR. A janela volta à lista de texto |
| S4b | IA mandar mensagem sozinha; janela de 24 h | Só proposta; envio pelo botão da equipe; janela respeitada | Não entra antes das condições |

## 10. Fora da v1

- Aniversário (pede campo novo e migração).
- Kanban de clientes.
- Colunas configuráveis.
- Foto do perfil do WhatsApp.
- Interesse de item na lista (endpoint `Admin`).
- Mensagem da IA a partir da lista (S4b), até as três condições.

## 11. Questões decididas

1. Trocar o cliente no Novo pedido, com itens já montados: decidido pelo Felipe em 23/11/2026, 18:22: mantém os itens e refaz a cotação.
2. Título da tela: decidido pelo Felipe em 23/11/2026, 18:23: continua "Leads e clientes".
3. Critérios do S4a: aprovados pelo Felipe em 23/11/2026, 18:25. Cadastro incompleto = sem telefone. Sem pedido parado = 0 compras e cadastro com mais de 30 dias. Vale a ordem de prioridade da tabela.

## 12. Referências

- Issues: #3237 (mãe), #3238 (S0), #2921, #3158, #3234 (PR #3236), #3193, #3076, #2777, #2946, #3155.
- ADRs: ADR-0055 (tier baixo para spec de plano aprovado), ADR-0070 (label `aprovado` delegada), ADR-0043 (PR-first).
- Specs: `docs/plan/novo-pedido-na-tela-2921.md` (emendada pelo S0).
- Protótipo clicável: artifact privado, validado com o Felipe em 23/11/2026. Não tem link neste documento.
