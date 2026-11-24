# Revisão do módulo Cadastros do console (2026-11-13)

Base: `origin/master` em `645fde9d5`. Tela conferida em produção (app.easystok.online), só leitura, nada salvo.
Escopo: `EasyStock.Console/src/features/gestao/cadastros/*`, `dominio/cadastros.js`, `aplicacao/cadastros.js`,
`infra/api/cadastrosApi.js` e os contratos de API que eles chamam.

## Veredito

O módulo FUNCIONA para listar e editar, mas está INCOMPLETO como cadastro e DESCUMPRE o design system em
pontos visíveis. Há 2 perdas silenciosas de dado e 1 provável erro 500. A causa de fundo é arquitetural:
um componente genérico decide tudo com `tela === '...'` (cerca de 30 ramificações), então cada entidade
recebe o mínimo comum e nenhuma fica completa.

## P1. Defeitos (corrigir primeiro)

| # | Defeito | Evidência | Efeito |
|---|---|---|---|
| B1 | Produto novo: `observacaoInterna` e `status` são enviados no POST, mas o `CadastrarProdutoCommand` não tem esses campos | `EditorCadastro.jsx:142-145`, `cadastrosApi.js:36`, `CadastrarProdutoUseCase.cs:7-32` | O texto digitado em "Observação interna" ao criar SOME sem aviso. Produto novo sempre nasce Inativo, e o campo Situação nem aparece na criação |
| B2 | Fornecedor: limites do front e do comando maiores que os do banco (e-mail 255 vs 150, telefone 20 vs 30) e PATCH sem validação | `EditorCadastro.jsx:129`, `FornecedorConfiguration.cs:11-12`, `AtualizarFornecedorUseCase.cs:6-23` | E-mail entre 151 e 255 caracteres deve virar erro 500 do banco |
| B3 | Fornecedor sem controle de concorrência (sem `revisao`) | `FornecedorController.cs`, `AtualizarFornecedorUseCase.cs` | Duas pessoas editando: a última apaga a outra sem aviso. Clientes e contas já têm 409 |
| B4 | Pílulas usam `tom="sucesso"` e `tom="alerta"`, que não existem no componente (aceita `ok`, `aviso`, `perigo`, `marca`, `neutro`) | `PainelCadastros.jsx:139`, `MetodosCadastro.jsx:33`, `ModelosCadastro.jsx:63`, `Pilula.module.css` | Em produção, Lead, Cliente, Ativo e Inativo aparecem como texto igual, sem marca de estado |
| B5 | Erro de validação por campo (`ValidationProblemDetails`) vira "Confira os campos." sem dizer qual | `infra/api/cliente.js:96`, editor só lê `e.message` | A pessoa não sabe o que corrigir |
| B6 | Clientes: sem checagem de telefone ou documento repetido | backend sem unicidade (`ClienteConfiguration.cs:23-26`), front sem aviso | Cadastro duplicado; em produção há 3 leads "Cliente WhatsApp" |

## P2. Incompleto (decisão de PO)

| # | Lacuna | API já suporta? |
|---|---|---|
| I1 | Não dá para inativar nem reativar cliente e fornecedor. O filtro "Situação" existe, a ação não | Sim: `DELETE /{id}` e `POST /{id}/reativar` |
| I2 | Subcategoria de produto não é selecionável (o front mostra só as raízes e zera `subcategoriaId`) | Sim, aninhada em `/api/categorias` |
| I3 | Não existe tela de categorias em lugar nenhum do console | Sim, CRUD completo |
| I4 | Ficha do cliente pobre: sem último pedido, lista de pedidos, vários endereços ou telefones. Endereço é um texto livre único, sem CEP nem bairro (Entregas depende de bairro) | Sim: `/enderecos`, `/telefones`, `/documentos`, `lastOrderAt` |
| I5 | Fornecedor sem histórico, estatísticas e prazo real médio | Sim: `/historico`, `/estatisticas`, `leadTimeRealMedioDias` |
| I6 | Lista sem ordenação | Sim: `sort` em clientes, fornecedores e produtos |
| I7 | "Produtos" mostra 2 itens e o Cardápio mostra 26 pratos. Os dois conceitos não são explicados na tela | Decisão de produto |

## P3. UX e UI

| # | Problema | Regra descumprida |
|---|---|---|
| U1 | Título repetido 3 vezes (barra, "CADASTROS", h2) | Hierarquia |
| U2 | Telefone exibido cru em E.164 (`+5511999990000`) e digitado sem máscara. CPF e CNPJ sem máscara | `CampoMascarado` e `mascaraTelefone` já existem |
| U3 | `maxLength` em quase todos os campos | Direção visual, seção 2: "sem maxLength nunca, corta o colado sem avisar" (`CampoMascarado.jsx`) |
| U4 | "Salvar cadastro" habilitado sem mudança; campos obrigatórios sem marca | Feedback de estado |
| U5 | Contas bancárias: busca diz "Nome, telefone ou documento" mas filtra nome e instituição; botão "Novo cadastro" genérico; estados vazios sem botão de criar | Microcopy |
| U6 | Pílula em toda linha | `Pilula.module.css`: "uma por linha, e só quando o estado pede ação" |
| U7 | Abas feitas com botões `aria-pressed` em vez de `BarraDeAbas` | Componente padrão e semântica de tablist |
| U8 | "Respostas automáticas" aparece em Cadastros › Modelos e no trilho lateral. A própria tela avisa que `{linkCardapio}` sai escrito literal para o cliente | Navegação duplicada; risco de mensagem quebrada se a resposta for ligada |
| U9 | Linha da tabela não é clicável; só o botão Editar abre | Alvo de clique |

## P4. Arquitetura e código

| # | Achado | Proposta |
|---|---|---|
| A1 | `PainelCadastros` e `EditorCadastro` com cerca de 30 `tela === '...'` e configuração espalhada em 8 mapas (`ICONE`, `CHAVE_LEITURA`, `CHAVE_EDICAO`, títulos, subtítulos, placeholders, rótulos, colunas) | Um descritor por entidade em `dominio/cadastros/<entidade>.js` (rota, colunas, campos, regras, permissões, ações) e componentes genéricos que leem o descritor (OCP, SRP) |
| A2 | Duas regras para o mesmo cliente: `dominio/validacaoDoCadastro.js` (ficha: nome até 80, telefone com DDD) e validação inline do editor (nome até 150, telefone livre) | Uma fonte de regra no domínio, usada pelos dois |
| A3 | Validação do formulário escrita dentro de `salvar()` no componente | Mover para o descritor e testar no domínio |
| A4 | Linhas de JSX com mais de 400 caracteres; truque de cancelar por `dispatchEvent` repetido 4 vezes; `pageSize` 20 fixo em 2 lugares | Extrair componentes e constante |
| A5 | PATCH de fornecedor responde 204 e o de cliente 200; fornecedor inexistente dá 400 em vez de 404 | Alinhar contrato |

## P5. QA

| # | Achado |
|---|---|
| Q1 | Nenhum teste de componente roda no CI. Só asserts de domínio (`prova-2395-cadastros.mjs`); as provas de UI são opt-in |
| Q2 | Dado de homologação em produção: "Homologacao PR2143 Cliente 53ec15da" |
| Q3 | Não validado: largura de celular (o redimensionamento do navegador não aplicou). O CSS esconde a coluna Contato abaixo de 700 px |

## Plano sugerido

| Onda | Conteúdo | Tier (ADR-0055) |
|---|---|---|
| 1. Correções | B1, B4, B5, U1 a U5, U7, U9, U2 com máscara | Baixo (fix) |
| 2. Backend | B2 e B3 (migração de limite e revisão de fornecedor), A5 | Alto (migração) |
| 3. Completar | I1, I2, I3, I6, aviso de duplicado (B6) e ficha do cliente (I4) | Alto sem spec; precisa spec em `docs/plan/` |
| 4. Arquitetura | A1 a A4 com testes de componente no CI (Q1) | Alto (refactor) sem spec |
