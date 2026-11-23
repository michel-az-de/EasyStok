# Relacionamento do cliente e Novo pedido com o cardápio (#3237)

- Data: 2026-11-23
- Base medida: `origin/master` `1aa58acbe` (inclui a #3234, PR #3236)
- Issue mãe: #3237. Esta spec é o S0, issue #3238.
- Relacionadas: #2921 (Novo pedido na tela, spec `novo-pedido-na-tela-2921.md`, emendada aqui), #3158 (momento de abrir a conversa, aberta), #3234 (crítica do Atendimento, aberta, PR #3236 mergeada), #3193 (vocabulário "Sem pedido"), #3076 e #2777 (Cadastros e regra de compra).
- Status: proposta aprovada por banca independente em 23/11/2026, com ajustes (seção 3). Protótipo clicável validado com o Felipe no mesmo dia, com dados fictícios. O protótipo é um artifact privado e não é citado por link.
- Tier desta PR: baixo (docs, ADR-0055). As fatias têm tier próprio na seção 5.
- Atlas: busca por EasyStok voltou vazia (lacuna declarada). Fonte usada: repositório, issues e leitura somente de produção (contagens e índices).

## 1. Problema

Duas dores, um objetivo: a equipe ENXERGA e AGE sobre a relação de cada pessoa com a loja sem abrir a Ficha, e monta pedido do MESMO jeito em qualquer tela.

A. Em Cadastros, "Leads e clientes" é uma lista de cadastro, não de relacionamento. O grid tem 4 colunas (Nome com ícone genérico e "N pedidos", Contato, Situação, Ações; `EasyStock.Console/src/features/gestao/cadastros/PainelCadastros.jsx`, linhas 218 a 235). Não mostra a foto, o gasto, as compras, o ticket médio, a última compra, a reclamação, as notas nem a próxima ação. Tudo isso já existe, só que por cliente (Ficha e Atendimento), não em lote.

B. A janela Novo pedido monta os itens com uma lista de texto e "+" (`EasyStock.Console/src/app/NovoPedidoJanela.jsx`, passo `itens`, linhas 215 a 230). A tela de Atendimento usa outro cardápio, com foto, grupos por categoria e porções como botões com preço (`EasyStock.Console/src/features/cardapio/PainelCardapio.jsx`). Quem usa as duas telas vê dois jeitos de fazer a mesma coisa.

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
| Foto do cliente | `Cliente.FotoChave`, `ClientesFotoController` (`api/clientes/{clienteId}/foto`), `EasyStock.Console/src/aplicacao/useFotoCliente.js` | o hook carrega a imagem com JWT e `no-store`; a lista já traz a URL, o grid é que não desenha |
| Dossiê | `ClientesCrmController` (`Operador`) | existe por cliente (Ficha e Atendimento), não em lote |
| Reclamação | entidade `Ocorrencia` (`EasyStock.Domain/Entities/Atendimento/`), `OcorrenciasController` | `Operador`. Produção: 0 registros |
| Notas e tags | `RelacionamentoCliente.jsx` (editor do cadastro) e `ClientesCrmController` | por cliente |
| Assistente | `AtendimentoAssistenteController` (`api/atendimento/assistente`, rotas `cadastro` e `ficha`) | recebe `ConversaId`, não cliente. Não existe nada na lista de clientes |
| Interesse de item | `ClienteInteressesController` | policy `Admin` |
| Cardápio do Atendimento | `PainelCardapio.jsx`: `FotoDoItem` (linha 57), `ItemEscolher` (124), `PorCategoria` (288) | completo, mas interno ao painel |
| Novo pedido | `NovoPedidoJanela.jsx`, domínio `dominio/cardapio.js` e `dominio/filtroDoCardapio.js` | lista própria de texto, mesmo domínio de cálculo (`filtrarCardapio`, `porcaoDoToque`) |
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
- O S1 devolve um record próprio da lista. Não se engorda o `ClienteResult`, usado em outros fluxos (criar, atualizar, buscar).
- O parâmetro `relacionamento` da lista (`lead`, `cliente`, `recompra`) já tem significado. Os novos filtros por estágio e risco usam parâmetro novo, sem reaproveitar esse.
- O nome `RelacionamentoCliente.jsx` já existe (notas e tags do editor). O módulo novo de domínio é `dominio/relacionamento.js` (puro: barras, rótulos), com nome distinto para não confundir.
- O Atendimento não muda visualmente com S5. A prova de regressão é obrigatória.
- Não se mexe no momento de abrir a conversa do Novo pedido (#3158), nem em tier ou critérios da #2921.
- Título da tela (`TITULOS_CADASTROS.clientes` = "Leads e clientes", `EasyStock.Console/src/dominio/cadastros.js`, linha 258): decidir fora do caminho crítico (questões abertas).

## 5. Fatias

| id | O que entrega | Arquivos prováveis | Aceite verificável | Tier | Depende de |
|---|---|---|---|---|---|
| F | Foto (ou iniciais) em cada linha do grid de clientes, carga preguiçosa | `PainelCadastros.jsx`, `cadastros.module.css`; reaproveita `componentes/Avatar` e `aplicacao/useFotoCliente.js` | Red: prova mostra o grid sem foto. Green: cliente com `fotoUrl` mostra a imagem; sem foto mostra iniciais; foto só carrega para a linha visível; sem mudança de API; prova de navegador em 1920 e 1440 | baixo | nenhuma |
| S1 | A lista devolve por linha: gasto total, compras, ticket médio, primeira e última compra, ocorrências abertas e total, notas (total e última curta), até 3 tags, estágio e em risco. Ordens "maior gasto" e "ticket médio". Record próprio | `EasyStock.Application/UseCases/ListarClientes/ListarClientesUseCase.cs`, `IClienteRepository.cs`, `EasyStock.Infra.Postgre/Repositories/ClienteRepository.cs`, `EasyStock.Api/Controllers/ClientesController.cs` | Red: teste de repositório e de API falham sem os campos. Green: valores batem com a `RegraDeCompra`; estágio 0, 1, 2 a 4 e 5 ou mais; em risco igual à `RegraDeRecompra`; teste com dois inquilinos prova que toda subconsulta filtra `EmpresaId`; sem migração; página de 200 linhas sem consulta por linha (N+1) | baixo | F (ordem de entrega, não técnica) |
| S2 | Linha nova do grid: foto, nome, "N pedidos", gasto, ticket, última compra (há N dias), cliente desde, barra de estágio com alerta Em risco, barra de gasto em % da carteira, sinal de reclamação e notas, tags | `PainelCadastros.jsx`, `cadastros.module.css`, `EasyStock.Console/src/dominio/cadastros.js`, novo `EasyStock.Console/src/dominio/relacionamento.js`, tradução em `EasyStock.Console/src/infra/api` | Red antes: provas de domínio de `relacionamento.js` falham. Green: o Console desenha o estágio que a API mandou (sem recalcular); prova de navegador 1920 e 1440 sem rolagem horizontal e sem sobreposição; 390 não quebra; estados vazio, carregando e erro | baixo | S1 |
| S3 | Faixa de indicadores no topo: ativos, % recorrentes, ticket médio, em risco, reclamações abertas; tocar num indicador filtra a lista | `ListarClientesUseCase.cs` e controlador (endpoint de indicadores), `PainelCadastros.jsx`, `cadastros.module.css` | Red: teste do endpoint falha. Green: números iguais à soma das linhas da lista do mesmo filtro; filtra `EmpresaId` (teste de dois inquilinos); indicador filtra a lista e o filtro aparece e se limpa; prova 1920 e 1440 | baixo | S2 |
| S4a | Coluna "Próxima ação" por regra no servidor: recompra devida, reclamação aberta, cadastro incompleto, sem pedido parado. Ações rápidas: Novo pedido, Conversa, Ficha | `ListarClientesUseCase.cs`, `PainelCadastros.jsx`; reaproveita `useNovoPedidoDoCliente` | Red: teste de unidade da regra falha. Green: cada regra produz a ação e o motivo esperados; prioridade fixa e documentada; a ação "Novo pedido" abre a janela da #2921 sem trocar a hash; sem IA | baixo | S3 |
| S5 | Cardápio de escolha compartilhado: extrair `ItemEscolher`, `PorCategoria`, `FotoDoItem` e a busca de `PainelCardapio.jsx` para `EasyStock.Console/src/features/cardapio/EscolhaDoCardapio.jsx` | `PainelCardapio.jsx`, novo `EscolhaDoCardapio.jsx`, `cardapio.module.css` | Red: prova de regressão do Atendimento capturada ANTES da extração. Green: o Atendimento fica igual (mesma prova, mesmos prints em 1920 e 1440); sem mudança de comportamento. O PR cita as mudanças do Atendimento já mergeadas (#3234) | baixo | nenhuma (frente independente) |
| S6 | Novo pedido com o cardápio do Atendimento: coluna Itens usa `EscolhaDoCardapio`; coluna Pedido com miniatura e contador; cabeçalho com foto e "costuma pedir" | `EasyStock.Console/src/app/NovoPedidoJanela.jsx`, `novoPedidoJanela.module.css` | Red: prova nova falha com a lista de texto. Green: foto, grupos por categoria e porções como botões com preço; "N no pedido" com "-"; busca por prato, categoria ou porção; o motor da #2921 (comanda, cotação, registro, resumo) não muda; não muda o momento de abrir a conversa (#3158); provas da #2921 seguem verdes; prova 1920 e 1440 | alto (spec 2921, seção 9) | S5 |
| S4b | "Sugerir com a IA" a partir da lista, com mensagem proposta e envio pela equipe | A definir | CONDICIONADA: só entra com as três coisas abaixo decididas em spec própria | alto | S4a e as três condições |

Condições do S4b (todas):

1. Contexto por cliente na API. Hoje o Assistente recebe `ConversaId`, não cliente (`AtendimentoAssistenteController`). Sem conversa não há contexto.
2. Canal fora da janela de 24 h: `wa.me` (a equipe envia pelo próprio aparelho) ou modelo aprovado no WhatsApp. Sem isso a mensagem para cliente inativa não sai.
3. Teto de uso (custo e latência controlados, ação sob demanda, uma cliente por vez).

Tier: F e S1 a S5 são tier baixo pela ADR-0055 (spec de plano aprovado em `docs/plan/**`, merge no verde). S6 é tier alto por herdar a spec 2921 (feat na janela Novo pedido): a PR fica aberta até a label `aprovado`, nas condições da ADR-0070.

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
| S5 | Regressão silenciosa no Atendimento | Prova antes e depois com prints; colisão com #3234 tratada: a PR dela já está mergeada e a extração parte do master atual | Reverter a PR. O Atendimento volta ao componente interno |
| S6 | Colisão com #2921, #3158 e #3076; mensagem acidental | Motor intocado; provas da #2921 verdes; sem campo de texto na janela | Reverter a PR. A janela volta à lista de texto |
| S4b | IA mandar mensagem sozinha; janela de 24 h | Só proposta; envio pelo botão da equipe; janela respeitada | Não entra antes das condições |

## 10. Fora da v1

- Aniversário (pede campo novo e migração).
- Kanban de clientes.
- Colunas configuráveis.
- Foto do perfil do WhatsApp.
- Interesse de item na lista (endpoint `Admin`).
- Mensagem da IA a partir da lista (S4b), até as três condições.

## 11. Questões abertas

1. Trocar o cliente no Novo pedido, com itens já montados: mantém os itens ou zera? Decidir antes de abrir a PR do S6. Sugestão provisória: manter os itens e refazer a cotação, avisando na janela; a decisão é do Felipe.
2. Título da tela "Leads e clientes": manter, ou trocar (por exemplo "Clientes") agora que o estágio "Sem pedido" substitui "Lead" na lista? Não bloqueia nenhuma fatia.

## 12. Referências

- Issues: #3237 (mãe), #3238 (S0), #2921, #3158, #3234 (PR #3236), #3193, #3076, #2777, #2946, #3155.
- ADRs: ADR-0055 (tier baixo para spec de plano aprovado), ADR-0070 (label `aprovado` delegada), ADR-0043 (PR-first).
- Specs: `docs/plan/novo-pedido-na-tela-2921.md` (emendada pelo S0).
- Protótipo clicável: artifact privado, validado com o Felipe em 23/11/2026. Não tem link neste documento.
