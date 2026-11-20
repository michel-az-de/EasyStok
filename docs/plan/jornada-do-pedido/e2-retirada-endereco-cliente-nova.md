# E2: Retirada no balcão, escolha de endereço e cliente nova na janela (#2944)

- Data: 2026-11-20
- Base medida: `master` `6c93bfe0a`; última migração `20261120123520_OrigemDoAnuncio`
- Issue: #2944 (plano). Relacionadas: #2919, #2921 (fase 2), #2776, #2835.
- Status: proposta. Tier alto (migração, contrato de API). PR aberta até a label `aprovado`, que só o Felipe aplica.
- Pré-requisito: E1 mergeada (reaproveita `motivo`, `rotulos.js` e `nomeExibido`) e a decisão da secção 2.
- Uma PR completa, com as 3 funções juntas, porque compartilham o passo Entrega e o contrato.

## 1. Problema

A janela fixa `const entrega = 'entrega'` (`EasyStock.Console/src/app/NovoPedidoJanela.jsx:55`): loja de balcão não registra retirada, cliente com dois endereços só usa o padrão e cliente nova sai do fluxo para cadastrar em outro lugar. Os ganchos já existem pela metade: `aplicacao/janelaDoPedido.js:60-61` tem `entrega !== 'retirada'` em `podeAvancarDaEntrega`, `GerarPedidoConversaInput` aceita `EnderecoId` (`GerarPedidoConversaUseCase.cs:27`) e `POST /api/clientes` existe (`ClientesController.cs:85`). Falta o domínio guardar a modalidade e o Console usar o que a API já oferece.

## 2. Decisão pendente do Felipe
**Decidido pelo Felipe em 2026-11-20:** opção A (coluna `Modalidade` no Pedido, com migração). O horário da retirada fica na janela: a retirada também escolhe `JanelaId` e `DataEntrega`, que continuam obrigatórios no input. Não há janela especial nem campo novo de horário.

| Opção | Efeito | Custo |
|---|---|---|
| A. Coluna `Modalidade` no Pedido (RECOMENDADA) | Estado explícito e consultável. Pedido de balcão legado NÃO vira retirada: recebe o default Entrega | 1 migração reversível |
| B. Derivar retirada da ausência de `EnderecoEntrega` | Sem migração | Pedido legado sem endereço e cliente sem endereço viram retirada por engano; sem como filtrar da rota de entrega |

Este documento segue a opção A. Se a resposta for B, os itens E2-01 e E2-02 caem e os riscos mudam; reabrir a spec.

## 3. Itens

Caminhos do backend relativos a `C:\rep\EasyStok`; do Console, a `EasyStock.Console/src`.

**E2-01 Enum e coluna `Modalidade`**
- `Domain/Entities/Pedido.cs:45` só tem `EnderecoEntrega`. Depois: enum `ModalidadeDoPedido { Entrega = 0, Retirada = 1 }` e propriedade `Modalidade` no `Pedido`, default Entrega.
- Migração `Modalidade` int não nulo, default 0, com `Down` que remove a coluna. Nenhum backfill: legado fica Entrega.
- Red: teste de domínio cria pedido de retirada e espera `Modalidade == Retirada`; falha por não existir.
- [ ] Migração aplica e reverte em Postgres limpo e em cópia do dump.

**E2-02 Regras da retirada no caso de uso**
- `EasyStock.Application/UseCases/Atendimento/Comanda/GerarPedidoConversaUseCase.cs`: o input (`:19-27`) tem `JanelaId` e `DataEntrega` não anuláveis (`:25-26`) e o endereço cai no padrão ou lança (`:91-92`). Depois: `Modalidade` no input; Retirada dispensa `EnderecoId`, frete zero, sem cotação. `JanelaId` e `DataEntrega` viram anuláveis no input; Entrega continua exigindo os dois (validação no caso de uso, 400 na API). Na Retirada, qual dos dois guarda o horário de retirada é decisão do Felipe junto com a secção 2; sem ela, o Red deste item não começa.
- Red: teste de uso gera retirada sem endereço e espera total sem frete; teste de Entrega sem endereço continua rejeitando; teste de Entrega sem `JanelaId` rejeita.
- [ ] Entrega mantém todas as regras atuais (nenhum teste existente enfraquecido).
- [ ] Pedido legado (gravado antes da migração) lê `Modalidade = Entrega` e segue em todas as telas sem mudança.

**E2-03 Contrato da API**
- Resposta de pedido e de simulação expõem `modalidade`; `POST /api/atendimento/conversas/{id}/pedido` e `/pedido/simulacao` aceitam `modalidade` (omitido = entrega, compatível com Console antigo).
- Red: teste de integração com Postgres: POST de retirada devolve `modalidade: "retirada"` e persiste.
- [ ] Console antigo (cache) continua funcionando sem enviar o campo.

**E2-04 Retirada na janela**
- `NovoPedidoJanela.jsx:55`: remove a constante; seletor "Entrega ou Retirada" no passo Entrega, usando o gancho de `janelaDoPedido.js:60-61`. Retirada esconde endereço e frete, mostra "Retirada no balcão" e o horário. Rascunho guarda a modalidade; rascunho antigo sem o campo abre como Entrega.
- Red: teste puro de `podeAvancarDaEntrega` com retirada e sem endereço espera `true`; hoje a janela nunca chega nele.
- [ ] Resumo à cliente diz "Retirada no balcão" e não cita frete.

**E2-05 Escolha de endereço**
- O Console nunca envia `enderecoId`; a janela edita um texto único. Depois: lista os endereços da cliente (`ClienteEndereco`, `Padrao`, `Cliente.cs:135,374,392`), marca o padrão, permite escolher outro e envia `enderecoId`. "Cadastrar endereço" (E1-01) segue para quando não há nenhum. Os endereços vêm do `GET /api/clientes/{id}` existente (`EasyStock.Api/Controllers/ClientesController.cs:75`); sem rota nova.
- Red: teste de integração: pedido com `EnderecoId` de um endereço não padrão grava esse endereço; prova de Console: trocar endereço muda frete cotado.
- [ ] Cliente com 2 endereços escolhe qualquer um; a cotação refaz.

**E2-06 Cadastro rápido de cliente nova**
- `PainelPedidos.jsx:85` e a escolha de cliente: hoje só `cadastrarClienteDaConversa` (`aplicacao/api/cliente.js:74`), que exige conversa. Depois: "Cliente nova" com nome e celular, usando `POST /api/clientes` via `salvarCadastro` (`infra/api/cadastrosApi.js:67`), abrir a conversa por baixo (`abrirConversaDoCliente`) e seguir para Itens. Celular validado como em #2932; duplicidade avisa com o padrão `.avisoDuplicado`.
- Red: prova Playwright: cliente nova, registrar pedido, ver em Pedidos. Teste de API para celular duplicado.
- [ ] Venda para cliente nova sem sair da janela.

**E2-07 Retirada nas telas que leem pedido**
- Rota de Entregas (`infra/api/entregasApi.js` e a consulta da API que a alimenta) e cobrança: retirada não entra em "Saiu para entrega"; fluxo termina em Pronto e Entregue (retirada). Cozinha (KDS) recebe igual.
- Red: teste de integração: retirada não aparece na rota de entregas.
- [ ] Retirada percorre Aguardando, Em preparo, Pronto, Entregue sem passar por rota.

## 4. Testes

| Camada | O quê |
|---|---|
| Domínio | `Pedido` com modalidade; Entrega sem endereço segue inválida; Retirada válida sem endereço |
| Caso de uso | `GerarPedidoConversa` com as duas modalidades e `EnderecoId` |
| Integração (Postgres) | Migração sobe e desce; POST e GET com `modalidade`; endereço não padrão; cliente nova; retirada fora da rota |
| Contrato | Console antigo sem `modalidade` = entrega |
| Console | `prova-2944-retirada.mjs`, `prova-2944-endereco.mjs`, `prova-2944-cliente-nova.mjs` (Red antes, Green depois) |
| Gate | `build-check.ps1`, suíte de API e CI verdes; CI verde não prova mergeabilidade: conferir `mergeStateStatus` |

## 5. Migração e deploy

1. Rodar a migração em cópia do dump de produção; medir tempo e conferir `Down`.
2. PR aberta, `aprovado` do Felipe, merge.
3. Dump de produção ANTES do deploy (anexar o caminho no comentário da PR).
4. Deploy por `scripts/deploy/vps-deploy.sh` e `console-deploy.sh`. `dotnet ef database update` manual exige GO do Felipe na sessão.
5. Saúde da API não prova a feature: validar a funcionalidade.

## 6. Validação em produção

Com a loja, em 390 px e desktop, com print: (a) retirada registrada e visível em Pedidos, Ficha e Cozinha, fora da rota de entregas; (b) cliente com 2 endereços escolhendo o não padrão, frete refeito; (c) cliente nova cadastrada e pedido registrado; (d) pedido legado de balcão continua Entrega; (e) resumo à cliente com o texto certo.

## 7. Rollback

| Camada | Passo |
|---|---|
| Console | Reverter o commit e rodar `console-deploy.sh`; o campo omitido volta a ser entrega |
| API | Reverter o commit e redeployar; a coluna extra é inofensiva para a versão anterior (tem default) |
| Banco | `Down` da migração só se necessário; pedidos de retirada já gravados perdem a distinção, então registrar quantos existem antes. Em último caso, restaurar o dump (exige GO) |

## 8. Riscos

| Risco | Mitigação |
|---|---|
| Migração em tabela grande | Coluna com default constante; medir em cópia do dump |
| Retirada vazando para a rota de entrega | E2-07 com teste de integração |
| Console antigo em cache | Campo opcional com default Entrega |
| Dois endereços e cotação de frete | Refazer a simulação ao trocar o endereço; prova de Console |
| Cliente nova duplicada | Aviso por celular, como na lista de Clientes |
| `JanelaId` e `DataEntrega` hoje obrigatórios (`:25-26`) | Viram anuláveis só no input; Entrega segue exigindo (teste); horário da retirada decidido com a secção 2 antes do Red |

## Fatos reconferidos no master (2026-11-20)

- `GerarPedidoConversaInput` exige `JanelaId` e `DataEntrega` não anuláveis (linhas 25 e 26). Para a retirada, os dois passam a ser opcionais quando `Modalidade = Retirada`, ou a retirada usa a data de retirada no lugar da janela. Isso entra no contrato da API.
- `EnderecoId` é opcional e, quando falta, vale `EnderecoPadrao(cliente)` (linha 91). Hoje um pedido sem endereço escolhido cai no padrão; se a cliente não tem nenhum, lança "Cadastre o endereço de entrega" (linha 92), regra que a Retirada precisa pular.
- Não existe `GET /api/clientes/{id}/enderecos`. Os endereços vêm no `GET /api/clientes/{id}` (linha 75). A escolha de endereço reusa esse GET, sem rota nova.
