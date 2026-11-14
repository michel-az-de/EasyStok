# Na conversa, quem tem cadastro mas nunca fez pedido aparece como lead (#2461)

Status: proposta, aguardando a aprovação do Felipe para D1 a D4. A2 da revisão R4 de Cadastros (#2395).

## Problema

A conversa classifica a pessoa só pela presença de `clienteId`:

- `EasyStock.Console/src/infra/api/traducaoConversas.js:142`: `conta: resumo.clienteId ? 'cliente' : 'lead'`
- `EasyStock.Console/src/dominio/vinculoCliente.js:6`: a mesma regra depois de uma correção de vínculo

No console, `conta === 'lead'` hoje quer dizer **sem cadastro**. É isso que mostra o selo "Contato" no
cartão da lista (`apresentacaoDoCartao.js:125`), o texto "Contato sem cadastro" no cartão do cliente
(`CartaoDoCliente.jsx:51`), a captura de dados e a conversão no primeiro Pix (`ehLead`, em 7 lugares).

O resultado é que uma pessoa que ganhou cadastro e nunca comprou não recebe nenhuma marca e passa por cliente.
A tela de Cadastros já faz essa distinção certa, nas abas Leads, Clientes e Sem cadastro, com
`ClienteRepository.cs:43-44`: é lead quem não tem pedido, e é cliente quem tem.

## Premissa refutada da issue

A issue previa um terceiro estado, "em verificação", para quando o histórico de pedidos ainda não foi
conferido. Ele **não é necessário**. A API pode responder `temPedido` em toda leitura com um `EXISTS` em
`pedidos`. Já existe o índice `IX_pedidos_ClienteId`, e a lista de conversas já faz subconsultas por linha,
como `PedidoStatus` e `FotoClienteChave` (`ConversaRepository.cs:183-215`). Também não serve o
`Conversa.HistoricoPedidosVerificado`: ele trata dos pedidos da conversa, não do cliente.

## Decisões propostas

| # | Decisão | Proposta | Alternativa descartada |
|---|---|---|---|
| D1 | O que conta como pedido | Qualquer pedido do cliente na empresa, inclusive cancelado. É a mesma regra das abas de Cadastros, e uma regra só para as duas telas | Ignorar cancelados: a conversa e Cadastros passariam a discordar sobre a mesma pessoa |
| D2 | Onde a API informa | `TemPedido` (`bool?`, nulo sem `clienteId`) no resumo da lista de conversas e no estado de vínculo (`EstadoVinculoCliente`), devolvido pela correção | Endpoint separado: seria mais uma chamada por cartão a cada polling de 5 s |
| D3 | Modelo no console | Campo novo `relacionamento`: `'sem-cadastro'`, `'lead'` ou `'cliente'`. O `conta` continua como está: `'lead'` significa sem cadastro | Mudar o significado de `conta`: mexeria na captura, na conversão do Pix e no resumo, que hoje funcionam |
| D4 | O que a tela mostra | Cadastro sem pedido: selo **Lead** no cartão da lista e "Cadastrado, ainda sem pedido" no cartão do cliente. Sem cadastro continua com "Contato" e "Contato sem cadastro". Cliente continua sem selo | Selo só na ficha: a lista é onde a dona decide quem atender primeiro |

## Regras

- `relacionamento` vem só da API. Se a API não informar `temPedido` (resposta antiga durante o deploy), o
  console usa o comportamento de hoje: `clienteId` vira `'cliente'`, e a falta dele vira `'sem-cadastro'`.
- Quando a dona corrige o vínculo, a resposta já traz o `temPedido` do cliente novo. Não existe estado
  intermediário.
- O primeiro pedido da pessoa vira `'cliente'` na próxima leitura da lista, em até 5 s. Não há evento novo.
- O telefone e o nome não mudam de lugar. Os campos novos são só booleanos e não levam dado pessoal.

## Fora do escopo

- Mudar `ehLead` ou os fluxos de captura, conversão no Pix e resumo do atendimento.
- O robô do WhatsApp tratar lead e cliente de forma diferente.
- Remover o GET `CandidatosLegado`, que tem item próprio no handoff da R4.

## Plano de entrega (uma PR)

1. **Red, API.**
   - Teste de integração da lista de conversas com 3 linhas: sem cadastro (`temPedido` nulo), cadastro sem
     pedido (`false`) e cadastro com pedido cancelado (`true`).
   - A mesma conta no estado de vínculo depois de `vincular`.
   - Isolamento: um pedido de outra empresa com o mesmo `ClienteId` não conta.
2. **Green, API.** `TemPedido` na projeção de `ConversaRepository` e em `ConversaInboxItem`,
   `ConversaResumoResult` e `EstadoVinculoCliente`. O predicado é o mesmo de `ClienteRepository.cs:43-44`.
3. **Red e Green, console.**
   - `relacionamentoDaApi` em `traducaoConversas.js` e `vinculoCliente.js`, com teste dos 3 estados e do
     fallback sem `temPedido`.
   - Selo **Lead** em `apresentacaoDoCartao.js` e texto no `CartaoDoCliente.jsx`.
4. **Gate e entrega.** `build-check.ps1` e o CI completo. Tier alto por ser feat que muda contrato, com merge
   depois da label `aprovado`. Deploy da API e do console por `vps-deploy.sh` e `console-deploy.sh`.

Não há migration. O contrato só ganha campos opcionais: um console antigo ignora `temPedido`, e um console
novo com uma API antiga cai no fallback.

## Aceite

- [ ] Na lista de conversas, um cadastro sem pedido mostra o selo Lead; sem cadastro mostra Contato; com pedido não mostra selo
- [ ] Depois de corrigir o vínculo para um cadastro sem pedido, o selo Lead aparece sem esperar o polling
- [ ] Um pedido cancelado conta como pedido, igual à aba Clientes de Cadastros
- [ ] Testes de integração com Postgres, incluindo o isolamento por empresa
- [ ] Captura, conversão no primeiro Pix e resumo do atendimento sem mudança (os testes atuais passam)

## Rollback

Reverter a PR. Não há dado gravado: os campos são calculados na leitura.
