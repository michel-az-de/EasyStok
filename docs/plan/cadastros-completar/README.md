# Cadastros do console: completar (onda 3)

- Data: 2026-11-13
- Base medida: `origin/master` `efc107517`
- Issue: #2495. Ondas anteriores: #2457 (onda 1), #2476 (onda 2).
- Origem: revisão de Cadastros de 13/11, itens I1, I2, I3, I4, I6 e B6.
- Status: aprovado pelo Felipe no chat em 2026-11-13 ("Aprovo, seguir"). Cada fatia entra como tier baixo (spec S/P de plano aprovado).

## 1. Decisões do Felipe (13/11)

| Pergunta | Decisão |
|---|---|
| Escopo | Onda 3 inteira: I1, I2, I3, I6, B6 e I4 |
| Duplicado (B6) | Avisa e deixa salvar: mostra quem já tem aquele telefone ou documento, com link. Sem backend novo |
| Forma | Esta spec com fatias; aprovada aqui, cada fatia segue tier baixo |

## 2. Princípio: só console

Todas as fatias usam rotas que a API já tem em `efc107517`. Nenhuma fatia muda backend, migração, policy ou workflow. Se uma fatia descobrir que precisa de backend, ela para e o ponto vira issue própria, tier alto.

## 3. Contrato da API usado

| Item | Rota | Detalhe que muda a tela |
|---|---|---|
| I1 cliente | `DELETE /api/clientes/{id}` (204) e `POST /api/clientes/{id}/reativar` (200) | Policy `Gerente`. Sem corpo e sem `revisao`. Idempotente. Grava auditoria `Ativo` |
| I1 fornecedor | `DELETE /api/fornecedores/{id}` (204) e `POST /api/fornecedores/{id}/reativar` (200) | Policy `Admin`. Com pedido Aberto ou EmTransito dá 400 com mensagem. Id inexistente dá 400 |
| I6 | `sort` e `order` em `GET /api/clientes`, `/fornecedores`, `/produtos` | Clientes: `nome`, `criadoem`, `lastorderat`, `ordercount`. Fornecedores: `nome`, `criadoem`. Produtos: `nome`, `criadoem`, `alteradoem`. Valor fora da lista cai no default da rota |
| I2 | `GET /api/categorias` | Devolve raízes com `subCategorias` de um nível. Produto aceita `subcategoriaId`; sub de outra raiz dá 400 |
| I3 | `POST`, `PUT {id}`, `DELETE {id}` em `/api/categorias` | Nome único por empresa, sem diferenciar maiúscula. Excluir com produto vinculado dá 400. Mover é `PUT` com `categoriaPaiId`. Não há limite de profundidade nem detecção de ciclo |
| B6 | `GET /api/clientes?search=` | Busca por contém em nome, documento e telefone (também `telefones[].numero`), ativos e inativos. Telefone guardado só com dígitos e `+` inicial; documento de 11 ou 14 dígitos guardado só com dígitos |
| I4 | `GET /api/clientes/{id}` e `POST`/`DELETE` em `/{id}/enderecos` e `/{id}/telefones` | A ficha traz `enderecos`, `telefones`, `orderCount` e `lastOrderAt`. Sub-recurso só adiciona e remove; não há edição. `Padrao` e `Principal` são gravados como vierem |
| I4 pedidos | `GET /api/pedidos?clienteId=&pageSize=5` | Ordem padrão: mais recente primeiro |

## 4. Fatias

### S1. Ordenação das listas (I6, feat, tier baixo)

- Escopo: cabeçalho clicável nas colunas que a API ordena, em Clientes, Fornecedores e Produtos. Alterna crescente e decrescente, com `aria-sort` e seta. A escolha fica na memória visual da tela (`aplicacao/cadastros.js`), como o filtro. O mapa coluna para `sort` mora no domínio.
- Fora: ordenar por coluna que a API não ordena (telefone, situação, preço). Pedidos já tem o Combobox "Ordem" e fica como está.
- Risco: o default de cada rota é diferente (clientes por nome, fornecedores e produtos por criação). A primeira abertura mantém o default de hoje, para a lista não mudar sem o usuário pedir.
- Aceite:
  - Red: prova de domínio falha porque não existe o mapa de ordenação; prova de navegador falha porque o clique no cabeçalho não envia `sort` e `order`.
  - Green: o clique envia `sort` e `order` certos, volta para a página 1 e marca `aria-sort`; ao reabrir a tela a ordem continua.

### S2. Inativar e reativar cliente e fornecedor (I1, feat, tier baixo)

- Escopo: no editor de um cliente ou fornecedor existente, botão "Inativar" (ou "Reativar" quando inativo) com confirmação em Modal, no padrão de `MetodosCadastro.jsx`. Depois da ação, a lista recarrega e a situação aparece certa. Erro da API aparece no Modal com o texto do servidor: 403 para quem não tem o nível, 400 do fornecedor com pedido aberto.
- Fora: inativar em lote; esconder o botão por nível (o console não expõe o nível hoje; o 403 explica). Produto já tem situação própria e fica fora.
- Risco: a ação não usa `revisao`. Se outro usuário editou o cliente ao mesmo tempo, o editor aberto fica velho; por isso o editor fecha depois da ação, em vez de seguir aberto.
- Aceite:
  - Red: prova de navegador falha porque não há botão Inativar no editor.
  - Green: confirmar envia `DELETE` (inativar) ou `POST /reativar`; cancelar não envia nada; o 400 do fornecedor mostra a mensagem do servidor e não fecha o Modal.

### S3. Subcategoria no produto (I2, feat, tier baixo)

- Escopo: no editor de produto, depois de Categoria, um seletor "Subcategoria" com as filhas da categoria escolhida, vindas de `GET /api/categorias`. Some quando a categoria não tem filha. Trocar a categoria continua zerando a subcategoria (já é assim no domínio).
- Fora: filtro da lista por subcategoria (a API só filtra por `categoriaId`).
- Risco: baixo. A API recusa sub de outra raiz com 400, e a tela só oferece as filhas da raiz escolhida.
- Aceite:
  - Red: prova de navegador falha porque não há seletor de subcategoria.
  - Green: escolher a subcategoria envia `subcategoriaId` no corpo; produto salvo com sub abre com ela marcada.

### S4. Tela de categorias de produto (I3, feat, tier baixo)

- Escopo: nova tela Cadastros › Categorias. Lista as raízes e, dentro de cada uma, as subcategorias. Ações: criar categoria, criar subcategoria dentro de uma raiz, renomear, mover uma subcategoria para outra raiz, excluir. A tela trabalha com dois níveis, que é o que o `GET` devolve.
- Fora: limiar de estoque (`PATCH /limiar`); categorias do Cardápio, que são outra entidade (`CardapioSecao`) e já têm tela.
- Riscos e guardas:
  - A API não impede ciclo nem terceiro nível. A tela só oferece raízes como destino de "mover" e não deixa mover uma raiz que tem filhas.
  - Não está medido o que o banco faz ao excluir uma raiz com filhas. A tela bloqueia esse caso com a mensagem "Mova ou exclua as subcategorias antes".
  - A API checa produto vinculado só por `CategoriaId`, não por `SubcategoriaId`. Antes de liberar excluir subcategoria, a fatia mede no Postgres local o que acontece quando há produto com aquela subcategoria. Se der erro de FK ou deixar produto órfão, a tela não exclui subcategoria em uso, e a correção da API vira issue própria.
- Achados de backend registrados, fora desta onda: o controller não tem policy (qualquer usuário autenticado cria e exclui); categoria inexistente dá 400 em vez de 404; nome sem índice único no banco.
- Aceite:
  - Red: prova de domínio da árvore e das regras de mover e excluir falha; prova de navegador falha porque a tela não existe.
  - Green: criar, renomear, mover e excluir enviam o corpo certo; nome repetido e categoria com produto mostram a mensagem do servidor; excluir raiz com filha fica bloqueado na tela.

### S5. Aviso de cliente duplicado (B6, feat, tier baixo)

- Escopo: no editor de cliente, ao sair do campo Telefone ou Documento, a tela busca `GET /api/clientes?search=<dígitos>&pageSize=10` e filtra no console só quem tem os mesmos dígitos, sem contar o próprio cliente. Se achar, mostra um Aviso: "Já existe {nome} com este telefone", com link que abre esse cadastro. Salvar continua liberado.
- Fora: unicidade no backend; fornecedor; mesclar cadastros.
- Riscos: a busca da API é por "contém", então o filtro exato no console é obrigatório, ou "1199" casaria com metade da base. Telefone com e sem `+55` são guardados diferentes; a comparação no console ignora o `55` inicial quando o número tem 12 ou 13 dígitos, igual à busca da API. A busca só dispara a partir de 8 dígitos de telefone e 11 de documento, e a resposta velha é descartada se o campo mudou.
- Aceite:
  - Red: prova de domínio de "mesmo telefone" e "mesmo documento" falha; prova de navegador falha porque nenhum aviso aparece com o telefone repetido.
  - Green: telefone repetido mostra o aviso com o nome e o link; o próprio cliente não conta; número parecido (contém) não avisa; salvar segue liberado.

### S6. Ficha do cliente (I4, feat, tier baixo)

- Escopo: no editor de um cliente existente, três seções abaixo dos dados:
  - Resumo: número de pedidos e data do último pedido (`orderCount`, `lastOrderAt`), e os 5 pedidos mais recentes com link para o pedido.
  - Endereços: lista os endereços com bairro e CEP; adicionar um endereço estruturado (logradouro, número, complemento, bairro, cidade, UF, CEP, referência) e remover. O primeiro endereço vai com `padrao: true`.
  - Telefones: lista os telefones extras; adicionar (com WhatsApp sim ou não) e remover.
  - O telefone e o endereço livre do próprio cliente continuam como estão.
- Fora: editar endereço ou telefone já gravado (a API só adiciona e remove: a tela oferece remover e adicionar de novo); trocar qual é o padrão; documentos extras; gravar bairro e CEP no próprio cliente (o `PUT` do cliente não aceita esses campos, seria backend).
- Riscos: o pedido usa o endereço estruturado só quando o fluxo escolhe aquele endereço (`EnderecoEntregaPedido`); esta fatia não promete taxa por bairro automática em todo pedido. A ficha de Atendimento (`features/ficha-cliente`) não muda; o que der para reaproveitar (máscaras, validação de CEP) vem de `validacaoDoCadastro.js`.
- Aceite:
  - Red: prova de navegador falha porque o editor não mostra pedidos, endereços nem telefones extras.
  - Green: a ficha mostra contagem, último pedido e os 5 pedidos; adicionar endereço envia o corpo com bairro e CEP e a lista atualiza; remover pede confirmação e envia `DELETE`; CEP inválido não envia.

## 5. Ordem

S1, S2, S3, S4, S5, S6. Uma PR por fatia, cada uma com prova Red antes do Green, gate e `/code-review`. O console vai ao ar ao fim de cada fatia mergeada (`console-deploy.sh`).

## 6. Aceite da onda

- As seis fatias mergeadas com CI verde.
- Console no ar com o SHA da última fatia (meta `easystok-console-sha`).
- Issue #2495 fechada com o aceite marcado.

## 7. Rollback

Cada fatia é só console e reverte com `git revert` do squash, seguido de `console-deploy.sh`. Nenhuma fatia grava dado novo fora das rotas que a API já expõe; endereços e telefones criados pela S6 ficam no cliente e podem ser removidos pela própria API.
