# E1: Novo pedido sem botão mudo, sem jargão e sem lacuna só-Console (#2944)

- Data: 2026-11-20
- Base medida: `master` `6c93bfe0a`
- Issue: #2944. Relacionadas: #2919, #2921, #2942.
- Status: proposta. Tier baixo (fix, chore e copy de Console, sem migração, sem API, sem processo). Merge no verde.
- Escopo: só `EasyStock.Console`. Uma PR, commits por item para facilitar o review.
- Caminhos relativos a `EasyStock.Console/src`. `(da auditoria)` marca linha não reconferida.

## 1. Problema

A janela Novo pedido tem 3 botões que ficam desabilitados sem dizer por quê, perde o foco ao trocar de passo e esconde itens e total no celular. Em volta dela, a tela fala "Comanda 21", "Cliente WhatsApp", "modelo aprovado" e "Assistente respondendo" com a IA desligada. Faltam ainda cancelar pedido em Pedidos, fechar a janela pelo botão voltar e prova de que a lista atualiza ao registrar.

Abordagem: uma regra por padrão (motivo sempre visível; glossário único; prova no navegador real) aplicada de uma vez, em vez de corrigir sintoma por sintoma.

## 2. Itens

Formato: arquivo:linha, antes para depois, teste Red, aceite.

### Bloco A: pacote visual (Impeccable)

**E1-01 Motivo nos desabilitados**
- `app/NovoPedidoJanela.jsx:141,142,167,181`: Continuar, Registrar, "+" e Cadastrar endereço ficam mudos. Depois: prop `motivo` do `Botao`: "Some ao menos um item", "Falta o endereço de entrega", "Escolha a forma de pagamento". Atualizar em Clientes (`PainelCadastros.jsx:194`) ganha `motivo` (da auditoria).
- Red: função pura `motivoDeBloqueio(estado)` em `aplicacao/janelaDoPedido.js` retorna `null` hoje para pedido vazio; o teste espera o texto.
- [ ] Todo botão desabilitado da janela mostra motivo visível ao lado, em 390 px e desktop.

**E1-02 Foco na troca de passo e no descarte**
- `NovoPedidoJanela.jsx:137-141`: o botão focado some e o foco cai no corpo (WCAG 2.4.3). Depois: ao mudar `passo`, foco no título do passo (`tabIndex={-1}`); no descarte, foco devolvido ao "Manter rascunho".
- Red: prova Playwright lê `document.activeElement` após Continuar; hoje é `body`.
- [ ] Após cada Continuar e Voltar, `activeElement` é o título do passo.

**E1-03 Faixa "N itens · total" e quantidade no cardápio**
- `NovoPedidoJanela.jsx:161-173,194`, `novoPedidoJanela.module.css:4,14`: abaixo de 640 px o cardápio vem antes do Pedido e o "+" não mostra nada; o total só existe no passo Revisar. Depois: `{qtd}x` na linha do cardápio e faixa fixa no rodapé em todos os passos, com `aria-live="polite"`.
- Red: prova de render: com 2 itens, o texto "2 itens" e o total existem no passo Itens.
- [ ] Faixa visível nos 3 passos; tocar "+" muda a faixa.

**E1-04 Quebra, linha editável e rodapé**
- `novoPedidoJanela.module.css:14`: `640px` para `759px` (Modal vira folha em 759, `Modal.module.css:62`). `:6` (da auditoria): `.lista li { flex-wrap: wrap }`, observação em `flex: 1 1 100%`. `:11-12`: `.erro, .aviso { flex: 1 0 100% }` no rodapé. Aviso com o padrão de `.avisoDuplicado` (`cadastros.module.css:54`, da auditoria).
- Red: prova que lê o CSS e falha enquanto existir `640px` no módulo.
- [ ] Em 700 px a janela já é folha de uma coluna; erro ocupa a linha inteira.

**E1-05 Estados: vazio, Esqueleto, "Calculando frete…"**
- `NovoPedidoJanela.jsx:98,163-169`: busca sem resultado e cardápio carregando ficam mudos. Depois: `Nada no cardápio com "{busca}".` e `EsqueletoDeLista`. `:95-100,193-194`: "Calculando frete…" (`role="status"`, total em `aria-busy`) derivado da chave de simulação no JSX, sem mexer no hook.
- Red: prova de render com busca "zzz" espera o texto vazio.
- [ ] Os 3 estados aparecem e somem corretamente.

**E1-06 Descartar com tom de perigo, "Tirar um {nome}", erro no campo**
- `NovoPedidoJanela.jsx:139`: Confirmar descarte com `variante="perigo"`, ambos `disabled={registrando}`. `:212`: `aria-label={`Tirar um ${nome}`}`. `:180-184`: `erro={erroEndereco}` no `CampoTexto` (hoje `role=alert` solto em `130` e `182-184`).
- Red: prova de render checa `aria-label` com nome e `disabled` durante `registrando`.
- [ ] Descartar não é clicável durante o registro.

**E1-07 Ficha: ordem dt/dd e foto**
- `features/.../ResumoDoCliente.jsx:63` (da auditoria): `<dd>` antes de `<dt>`. Depois: `<dt>` primeiro e `flex-direction: column-reverse` (`cadastros.module.css:132-134`). `:56,61`: "Remover foto" com confirmação.
- Red: prova de render checa a ordem no DOM.
- [ ] Leitor de tela lê rótulo e depois valor.

**E1-08 Checkbox, `small`, `--b-2`**
- `novoPedidoJanela.module.css:10`: checkbox nativo 13 px para o estilo `.check` (`cadastros.module.css:57-58`, da auditoria). `:3,11`: `2px` para `var(--b-2)`. `.lista small` com `--t-micro` e `--tinta-2`.
- Red: prova de CSS falha com `2px` literal no módulo.
- [ ] Checkbox de 20 px com `accent-color`.

**E1-09 Clientes no celular**
- `cadastros.module.css:33,88-92` (da auditoria): `nowrap` nos botões da linha. Depois: sem `nowrap` até 700 px; `.clicavel:focus-within td`. `PainelCadastros.jsx:196`: Carregando vira `EsqueletoDeLista` mantendo a tabela anterior.
- Red: captura em 390 px mostra "Continuar pedido" vazando (item "a medir" da auditoria).
- [ ] Nenhum botão vaza em 390 px; foco por teclado visível na linha.

**E1-10 Fila do Atendimento**
- `caixa.module.css:19` (da auditoria): animação em todo cartão a cada remonte. Depois: só cartão novo. `PainelConversas.jsx:164`: `acao="Tentar de novo"` no `VazioEmLinha`.
- Red: prova de render do erro de carga espera o botão.
- [ ] Erro de carga tem botão que refaz a busca.

### Bloco B: vocabulário e nome

**E1-11 Glossário único `dominio/rotulos.js`**
- Novo módulo com constantes: PEDIDO, PRAZO_24H, MENSAGEM_PADRAO, estados do pedido, verbos de cobrança. Importado nos arquivos alterados (cerca de 30).
- Texto como chave de lógica migra para código, sem trocar o texto da API: `motivoDeEscalada.js:40` (regex "cliente pediu ajuda pela comanda"), `falhaDeEnvio.js:24` (a regex já casa o código `fora_da_janela_24h` junto com o texto "fora da janela de 24 h"; passa a decidir primeiro pelo código e mantém o texto só como reserva; "modelo aprovado" está no rótulo de `:16`, que é exibição e pode trocar), `nomeDoContato.js` (`ehVisitante` casa `/^comanda\s+\d+$/`; passa a usar campo, não rótulo). `ferramentas/prova-2543-chip-do-motivo.mjs:84-85` trava a frase de `ComandaClienteUseCase.cs:713`: a prova continua apontando para a API, que não muda nesta PR.
- Red: prova `prova-2944-chaves.mjs` troca o rótulo no glossário e confirma que a classificação não muda.
- [ ] Trocar um rótulo não altera nenhuma decisão de lógica.

**E1-12 Troca de vocabulário (só Console)**
Pares (contagem da auditoria): Comanda para Pedido (84), janela 24 h para "prazo de 24 h" (12), modelo aprovado para "mensagem padrão do WhatsApp" (24), Telefone e Celular para "Celular (com DDD)" (22), "Saiu" para "Saiu para entrega" (`BlocoEntrega:36`), "Preparando" para "Em preparo" (já converge), cobrança em um rótulo só (`resumoPedido.js:29`, `BarraProximoPasso` 241 e 315, `BlocoCobranca:378`, `comanda.js:183`), "Registrar" para "Registrar pedido" (`NovoPedidoJanela.jsx:142`), "Marcar entregue mesmo assim" para "Entregar e cobrar depois" (`BarraProximoPasso:411`), "Não entregues" para "Mensagens não entregues", "Celular com DDD" para "Celular com DDD (para enviar o resumo)".
- NÃO trocar: `comanda_cliente` (chave de dado), `saiu_para_entrega` e `preparando` (chaves), Caixa "Entrou/Saiu" dinheiro, "Saiu certo?", `areaEntrega.js:82` (intenção do cliente), ícone e rota "Modelos aprovados", impressão (`impressao.js`, `ComandaHtml.cs`), mensagens da API.
- Red: guarda `verificar-gramatica.mjs` com os termos novos em `TERMOS` falha acima do teto; Green quando as ocorrências caem.
- [ ] Orçamento `termoProibido` baixa de 18 para o número medido ao final; teto nunca sobe.

**E1-13 Telefone no lugar de "Cliente WhatsApp"**
- `PainelCadastros.jsx:34`, `EscolherClienteDoPedido.jsx:52`, `ResumoDoCliente.jsx:28`, `CartaoDoCliente`: mostram `cliente.nome` cru. Depois: `nomeExibido(cliente)` em `dominio/nomeDoContato.js`, generalizando o que já existe para conversa (`NOME_PADRAO_DO_LEAD` e `nomeDaConversa`, `nomeDoContato.js:20-27`, #2276) em vez de criar regra paralela; `nomeDaConversa` passa a delegar a ela (nome vazio ou igual ao padrão com telefone válido: telefone formatado `+55 (11) 98888-7777` mais selo "Sem nome"; sem telefone: "Contato sem nome"). Valor no banco intocado.
- Red: teste puro de `nomeExibido` com "Cliente WhatsApp" espera o telefone.
- [ ] Nenhuma das 3 telas mostra "Cliente WhatsApp".

**E1-14 Vazio da Ficha com ação**
- `fichaCliente.js:60`, `ModalHistorico.jsx:48`: "Nenhum pedido ainda" para "Nenhum pedido ainda. Use Novo pedido para começar." com o botão **Novo pedido** no próprio vazio.
- Red: prova de render do vazio espera o botão.
- [ ] Vazio da Ficha abre a janela.

**E1-15 Texto desatualizado da escolha de cliente**
- `EscolherClienteDoPedido.jsx:40-41`: "O pedido abre na conversa do WhatsApp da loja ... no Atendimento" (decisão revogada pela #2921). Depois: "O pedido é montado nesta janela. A cliente recebe o resumo no WhatsApp ao registrar."
- Red: prova de leitura de fonte falha enquanto "no Atendimento" existir no arquivo.
- [ ] Texto descreve o fluxo real.

**E1-16 Rótulo honesto de estado da conversa**
- `dominio/automatico.js:74` 'Assistente respondendo': aparece com IA desligada. Depois: IA desligada e conversa assumida pela equipe mostram "Conversa assumida por você"; só IA ligada mostra "Assistente respondendo". Título "Comanda 21" para "Pedido de {nome}" (via `nomeExibido`).
- Red: teste puro do rótulo com `ia=false` espera "Conversa assumida por você".
- [ ] Nenhuma conversa com IA desligada diz "Assistente respondendo".

### Bloco C: lacunas só-Console da jornada

**E1-17 Cancelar pedido em Pedidos (G1)**
- `features/gestao/cadastros/PainelPedidos.jsx:118` só tem "Abrir". Depois: ação "Cancelar pedido" com confirmação, usando o `cancelarPedido(pedidoId, motivo)` existente (`infra/api/comandaApi.js:285`, ação em `aplicacao/api/comanda.js:371`; API `EasyStock.Api/Controllers/PedidosController.cs:126-129`, `Motivo` opcional no `CancelarPedidoCommand`). Sem motivo bloqueante: mostra por que não cancela (ex.: já entregue).
- Red: prova de render da linha espera a ação; teste puro da regra de elegibilidade.
- [ ] Cancelar pede confirmação, a lista atualiza e a Ficha reflete.

**E1-18 Voltar do navegador fecha a janela (G7)**
- Sem `popstate` em `componentes/Modal.jsx` nem na janela (só `hashchange` em `app/App.jsx:453`). Depois: abrir a janela empilha um estado de histórico; voltar fecha a janela e guarda o rascunho; a hash não muda.
- Red: prova Playwright: abre a janela, `page.goBack()`, espera a janela fechada e a tela de origem mantida.
- [ ] Voltar fecha só a janela; avançar não a reabre sozinha.

**E1-19 Registrar atualiza lista e Ficha (G8)**
- `aoRegistrar` (`NovoPedidoJanela.jsx:108`) só limpa o rascunho e fecha; `gerarPedido` recarrega só a conversa (`comanda.js:200`). Depois: dispara a recarga da lista Pedidos e do dossiê da Ficha, e destaca o pedido novo.
- Red: prova Playwright: registrar e ver o pedido na lista sem recarregar.
- [ ] Pedido novo visível na lista Pedidos e na Ficha da cliente após registrar.

**E1-20 Prova de chegada à Cozinha (G16)**
- `infra/api/kdsApi.js:10` lê `/api/kds/pedidos`. Depois: prova de ponta a ponta no navegador: registrar na janela e achar o pedido (status Aguardando) na Cozinha.
- Red: a prova falha enquanto o pedido não aparecer; é prova, não código novo.
- [ ] Prova arquivada em `ferramentas/prova-2944-cozinha.mjs`.

**E1-21 Aviso de cliente sem celular (G6)**
- Pedido para cliente sem celular segue sem aviso (#2919). Depois: linha no passo Revisar "A cliente não será avisada: falta o celular", com atalho para informar o número (reuso do #2932).
- Red: prova de render com cliente sem celular espera a linha.
- [ ] Nenhum pedido sai sem que a loja saiba que a cliente não será avisada.

## 3. Testes

| Camada | O quê |
|---|---|
| Unitário | `motivoDeBloqueio`, `nomeExibido`, rótulo de estado, elegibilidade de cancelar. Rodar com vitest se o runner existir no Console (a auditoria informa que não há `*.test.*`: confirmar; se não houver, vira `prova-2944-*.mjs` com `node:assert`, sem adicionar dependência) |
| Provas | `ferramentas/prova-2944-*.mjs` (chaves, foco, voltar, registrar, cozinha, render dos estados). Cada uma falha antes (Red) |
| Guarda | `verificar-gramatica.mjs`: termos banidos em `TERMOS`; `termoProibido` baixa de 18, nunca sobe |
| Regressão | Provas existentes que citam texto trocado (cerca de 25 arquivos de prova, 23 para "janela") são ajustadas no mesmo commit, sem enfraquecer |
| Build | `powershell -File scripts/poka-yoke/build-check.ps1` |

## 4. Validação no Chrome

Console servido do worktree da tarefa. Em 390 px e em desktop, antes e depois, com print de: botão com motivo, troca de passo com foco, faixa do total, janela em 700 px, busca vazia, "Calculando frete…", Descartar, Ficha, Clientes, fila com erro, lista de Pedidos com cancelar, voltar do navegador, nome por telefone. Pedido real de ponta a ponta: cliente com e sem celular, registrar, ver em Pedidos, Ficha e Cozinha. Depois do deploy, repetir uma vez em produção e anexar.

## 5. Rollback

Console estático: reverter o commit de merge e rodar `console-deploy.sh`. Sem migração, sem mudança de API, sem dado novo. Rascunhos guardados no navegador seguem compatíveis (formato do rascunho não muda).

## 6. Riscos

| Risco | Mitigação |
|---|---|
| Diff grande (cerca de 80 arquivos, 340 a 380 linhas só do vocabulário) | Commits por item; revisão por bloco; teto do orçamento da guarda |
| Trocar rótulo quebra lógica que casa texto | E1-11 migra para código antes de E1-12; prova de chaves |
| Prova existente fixa o texto antigo | Ajustar a prova no mesmo commit; nunca afrouxar asserção |
| Itens "a medir" (E1-04, E1-09) sem captura | Print em 390 px antes de codar; item cai se não houver problema real |
| Rótulo do E1-16 | Fontes divergem ("IA desligada" ou "Conversa assumida por você"); decidir pelo campo real no código antes de implementar |
| Mensagens da API ainda com jargão (ex.: 409 "frete a combinar") | Fora desta PR; PR de API própria depois |

## Fatos reconferidos no master (2026-11-20)

- O Console não tem vitest no `package.json`. Os testes são `ferramentas/prova-*.mjs` com `node:assert` (há 378). A E1 segue esse padrão: `prova-2944-*.mjs`.
- O rótulo em `src/dominio/automatico.js:74` é 'Assistente respondendo'. O texto novo sai do glossário em `rotulos.js`.
