# Banca de UX do Balcão: A. Inventário factual (leitura de código)

Base: worktree `C:\rep\EasyStok\.claude\worktrees\ux-balcao-leitura`, master `c59b696e5`. Front: `EasyStock.Console/src`.
Somente leitura. Nada foi editado, nenhum servidor foi subido, nenhum processo foi tocado.

## 0. Método, limites e como ler

| Item | Como foi medido |
|---|---|
| Contagens de botões, ícones só-ícone, textos de tela | Parser AST (`parseSync` do rolldown, já instalado no `node_modules` do console) sobre os 90+ `.jsx`; scripts em `scratchpad\banca\*.mjs` (`icones`, `somente_icone`, `copy`, `rotulos`, `densidade`, `movimento`, `tokens`, `dups`) |
| "Escopo Balcão" | `app/`, `caixa-de-entrada`, `atendimento`, `ficha-cliente`, `assistente`, `agente`, `lembretes`, `notificacoes`, `anexos`, `notas`, `encerramento`, `agora`, `ajuda`, `loja`, `lote-papel`, `automacoes`, `impressao-automatica`, `componentes/` (1.884 textos de tela extraídos; 377 botões) |
| FATO | Visto no código, com arquivo:linha ou contagem reproduzível |
| INFERÊNCIA/SUSPEITA | Marcada assim. Nenhuma tela foi aberta: densidade e hover/foco são estimativas pelo JSX e pelo CSS |
| Estática vs runtime | Contagens de "controles visíveis" são do JSX (teto) e da minha leitura de um estado típico: desktop, WhatsApp, janela de 24 h aberta, pedido criado aguardando pagamento |

Documentos de UX que já existem e que este inventário NÃO repete: `docs/plan/erp-casa-da-baba/13-critica-ux-desktop-2026-10-12.md` (rolagem, larguras, ordem dos blocos da Ficha), `docs/plan/erp-casa-da-baba/12-design-sprint-desktop-2026-10-12.md`, `docs/plan/erp-casa-da-baba/14-homologacao-gravada-2026-10-15.md` (ciclo de reabrir, impressão, nota, composer), `docs/plan/atendimento-whatsapp/10-console.md` (paridade API). `docs/plan/04-ux.md` é o wireframe da aba Pagamentos do backend, e ADR-0011/0018 são nomenclatura de domínio (rotulagem/NF-e): nenhum trata de nome de tela, ícone, tooltip ou copy do Balcão. Não existe doc de vocabulário ou de iconografia do console.

---

## 1. Mapa de telas

### 1.1 Rotas (hash) e módulos

Fonte: `dominio/rota.js:20-73`, `dominio/modulos.js:23-121`, `app/App.jsx:480-515`. Hash desconhecida cai no Hall (`rota.js:72`). Módulo congelado (#1891) vira Hall.

| Hash | Tipo (interno) | Nome que a pessoa vê | Observação |
|---|---|---|---|
| (vazio), `#/`, qualquer hash inválida | `ROTA_HALL` → `HallDeModulos` | Saudação (h1, `HallDeModulos.jsx:178`), cartões dos módulos, painel "Agora" (`PainelAgora.jsx:61`), números do Pulso (h2 oculto "Agora na casa", `PulsoDoHall.jsx:54`) | "Hall" e "Pulso" são só nome de código: não aparecem na tela. "Agora" aparece. |
| `#/m/atendimento/balcao` | `ROTA_PRINCIPAL` → `Composicao` → `Moldura` | No trilho: "Atendimento" (`Moldura.jsx:296`). `title="Balcão"` só no hover. | A tela de trabalho. Ver 1.2. |
| `#/m/atendimento/horarios` | `ROTA_MODULO` (ajuste, aba `atendimento`, só API) | "Horários e mensagens" | Abre dentro de `MolduraDoModulo` |
| `#/m/atendimento/avisos` | ajuste, aba `avisos`, só API | "Avisos" | Mesmo nome do sino do Balcão, mas é configuração de quem recebe aviso |
| `#/m/atendimento/respostas` | ajuste, aba `respostas` | "Respostas e automáticas" | Também abre como modal no Balcão (`App.jsx:296-305`) |
| `#/m/cozinha/fila` | `ROTA_COZINHA` | "Fila de preparo" | Aba "Produção e cardápio do dia" some na API |
| `#/m/entregas/painel`, `/agenda`, `/janelas` | `ROTA_ENTREGAS` / ajuste | "Painel de entregas", "Agenda de encomendas", "Janelas de entrega" | |
| `#/m/cardapio/{itens,categorias,imagens}` | ajuste (só API) | "Itens do cardápio", "Categorias", "Banco de imagens" | |
| `#/m/producao/{estoque,producao,insumos,receitas,calculadora,planejamento,perdas}` | ajuste (só API) | "Estoque do dia", "Produção do dia", "Insumos", "Receitas", "Calculadora", "Planejamento", "Perdas" | "producao" é id de módulo E de tela (colisão interna) |
| `#/m/financeiro/{caixa,visao-geral,pagar,receber,extrato,relatorios,categorias,centros-custo}` | ajuste | "Caixa do dia", "Visão geral", "Contas a pagar", "Contas a receber", "Extrato", "Relatórios", "Categorias financeiras", "Centros de custo" | |
| `#/m/campanhas/fidelidade` | ajuste | "Fidelidade e cupons" | indisponível na API |
| `#/m/configuracoes/{canais,integracoes,impressao,usuarios,auditoria}` | ajuste | "Canais", "Integrações", "Impressão", "Usuários", "Histórico de acesso" | O módulo mostra ícone de sliders (`MODULOS.icone: 'automacao'`, `modulos.js:99`). Nenhuma engrenagem existe no console. |
| `#/cozinha`, `#/entregas` | apelidos antigos (`rota.js:66-67`) | Fila / painel sem moldura | Janela avulsa do tablet; `abrirCozinha()` faz `window.open(..., 1280x900)` (`Moldura.jsx:166`) |
| `#/cardapio-link/<id>` | `ROTA_CARDAPIO_LINK` | Demonstração: `TelaCardapioLink`. API: `AvisoCardapioDoSite` | |
| `#/convite` (token some da barra) | `TelaConvite` | convite | `App.jsx:489-507` |
| sem sessão (API) | `TelaLogin` | login | |
| sessão sem permissão | `Vazio` | "Seu perfil não abre esta tela" (`App.jsx:576`) | menciona "a dona" |

### 1.2 Casca do Balcão por largura (`hooks/useTamanhoTela.js:7-11`)

| Faixa | Casca | Colunas/abas visíveis | Nomes na tela (visível) | Nomes internos |
|---|---|---|---|---|
| DESKTOP ≥ 1180 px | `Trilho` + `main` com 3 colunas (`Moldura.jsx:469-514`) | Lista de conversas, conversa, resumo do cliente | **Nenhum título de coluna é desenhado** (`semTitulo`, `Moldura.jsx:485,506`; só `aria-label` "Balcão", "Atendimento", "Resumo"). O único rótulo visível é o item do trilho: "Atendimento". | Coluna 1 = `entrada`/`caixa-de-entrada`/`PainelConversas`; coluna 2 = `PainelAtendimento`+`Thread`+`Composer`; coluna 3 = `PainelFicha`/`ficha-cliente` |
| TABLET 760-1179 px | Topo (`Cabecalho`, `Moldura.jsx:194-264`) + 2 colunas + rodapé | Balcão e Atendimento; "Resumo de {Nome}" abre uma gaveta (`Moldura.jsx:529-539`) | Títulos "Balcão" (visível aqui), "Resumo" | `Gaveta` = `Modal` com classe |
| CELULAR < 760 px | Topo + 1 painel + `BarraDeAbas` | Abas "Balcão" (com contador), "Atendimento", "Resumo" (`Moldura.jsx:578-580`) | idem | |
| Topo compacto < 1024 px | Menu "⋯ Mais" (`Moldura.jsx:76-158`) | Tema, Módulos, Entregas, Gestão, Ajuda, "Hoje: N em andamento · N na esteira", Conta/Sair | **"na esteira" vaza** (`Moldura.jsx:64-65`) | `MenuMais` |

### 1.3 Tudo que abre a partir do Balcão (painéis, modais, gavetas)

`<Modal>` (dialog nativo) no escopo Balcão: **36 usos**; mais **23** dentro da Gestão. Overlays que NÃO usam `Modal`: `ModalNota` (dialog manual, `notas/ModalNota.jsx:40`), `BalaoAssistente` (section role=dialog não modal), painel do `Sininho`, `PainelCardapio` (aside), `MenuDaConta` (Popover API), `Toast`, `FaixaApi`, `AtualizacaoDoConsole`.

| Gatilho (nome na tela) | O que abre | Título que a pessoa vê | Nome interno | arquivo:linha | Jargão |
|---|---|---|---|---|---|
| Trilho: "Módulos" / marca | vai ao Hall | (saudação) | Hall | `Moldura.jsx:276` | |
| Trilho: "Loja aberta/fechada" | alterna a loja; pede caixa/justificativa | "Abrir a loja", "Fechar a loja no horário", "Fechar o caixa de…" | `GestoLoja` | `Moldura.jsx:283`, `loja/GestoLoja.jsx:141,193,308` | "Gesto" interno |
| Trilho: "Entregas" | gaveta de entregas | "Entregas" | `GavetaEntregasApi` | `Moldura.jsx:298`, `App.jsx:309` | |
| Trilho: "Cozinha" | outra janela (1280x900) | "Fila de preparo" | `abrirCozinha` | `Moldura.jsx:302` | |
| Trilho: "Automáticas (n)" | modal | **"Mensagens automáticas"** (descrição: "Mensagens, horários e avisos da loja") | `ModalAutomacoes`, `automacao` | `Moldura.jsx:306`, `ModalAutomacoes.jsx:242` | botão e título diferentes; "automáticas" = só mensagens? |
| Trilho: "Gestão" | modal | **"Gestão da loja"** > "Áreas da loja" | `ModalGestao` | `Moldura.jsx:311`, `ModalGestao.jsx:96` | duplica o Hall inteiro dentro de um modal |
| Trilho: "Avisos" (sino) | modal | "Avisos" | `SinoDaSessao` | `Moldura.jsx:318`, `SinoDaSessao.jsx:34-40` | |
| Trilho: "Lembretes" (relógio) | painel flutuante + modal "Programar lembrete" | "Lembretes" | `Sininho` | `Moldura.jsx:319`, `Sininho.jsx:262-300` | arquivo chama "Sininho" mas o ícone é relógio (`Sininho.jsx:278`) |
| Trilho: "Ajuda" | modal | "Ajuda" > "Como usar esta tela" / "Guia do Console" | `AjudaDoConsole` | `Moldura.jsx:320`, `AjudaDoConsole.jsx:34` | "Console" |
| Trilho: "Simular" (F2) | painel (só demonstração) | | `PainelSimulacoes` | `Moldura.jsx:324` | |
| Avatar | menu: Tema (Claro/Escuro) + "Sair/Sair deste aparelho" | nome + casa | `MenuDaConta` | `Moldura.jsx:332` | |
| Lista: "Ordenar" | popover | "Urgência", "Mais recentes", "Entrega mais cedo" | | `BarraBalcao.jsx:317-351` | |
| Lista: faixa "N mensagens não entregues" | modal | "Não entregues" | `NaoEntregues` | `NaoEntregues.jsx:314,321` | |
| Lista: busca sem conversa | "Chamar no WhatsApp" | modal "Chamar no WhatsApp" / "Enviar modelo aprovado" | `ChamarClientes`, `ModalEnviarModelo` | `ChamarClientes.jsx:45`, `ModalEnviarModelo.jsx:81` | "modelo aprovado" = template da Meta |
| Cabeçalho da conversa: "Link de compra" | modal | "Link de compra" | `CompartilharComanda`, `ModalEnviarCardapio` | `PainelAtendimento.jsx:134`, `ModalEnviarCardapio.jsx:60` | arquivo "EnviarCardapio" |
| Cabeçalho: "Passar para…" | modal | "Passar a conversa de {Nome}" | `PassarConversa` | `PassarConversa.jsx:165,170` | |
| Cabeçalho: "Encerrar" | modal | "Resumo do atendimento {n}" / "Encerrar atendimento" | `ModalEncerrar` | `PainelAtendimento.jsx:149`, `ModalEncerrar.jsx:158` | |
| Cabeçalho: "Assistente" | painel flutuante encaixável | "Assistente" | `BalaoAssistente` | `BalaoAssistente.jsx:246-273` | ícone `agente` |
| Faixa de modo: "Devolver ao automático" | modal de confirmação | "Devolver ao atendimento automático?" | `BarraModo` | `BarraModo.jsx:82-99` | |
| Composer: "Respostas" (ou `/`) | popover | "Respostas rápidas" (aria: "Respostas prontas") | `SeletorRespostas` | `Composer.jsx:238`, `SeletorRespostas.jsx:28` | |
| Composer: "Respostas" > "Gerenciar respostas" ou clique no selo "automática" | modal | "Respostas e automáticas" | `PainelDeAjuste aba=respostas` | `App.jsx:296-305` | terceira porta para a mesma config |
| Composer: "Anexar" | seletor de arquivo do sistema | | `BotaoAnexarArquivo` | `ComposerAnexos.jsx:177` | |
| Composer: "Fotos" | modal | "Fotos e peças" (+ "Nova peça"/"Editar X") | `PainelGaleria` | `PainelGaleria.jsx:171,89` | "peça" |
| Composer: "Programar" | modal | "Programar mensagem" | `ModalProgramarMensagem` | `ModalProgramarMensagem.jsx:118` | |
| Composer: "Nota" | post-it | "Nota interna · … nunca vê" ; botão "Fixar nota" | `ModalNota` | `ModalNota.jsx:40-59` | "Fixar" |
| Composer (janela fechada): "Modelos aprovados" | modal | "Enviar modelo aprovado" | `ModalEnviarModelo` | `Composer.jsx:169` | |
| Resumo: "Adicionar itens"/"Ver cardápio" | painel lateral não modal | "Cardápio de hoje" ou "Consulta do cardápio" | `PainelCardapio` | `PainelCardapio.jsx:647-663` | dois títulos para o mesmo painel |
| Resumo: cartão do cliente | modal | "Cliente e histórico · {Nome}" | `BlocoCliente` | `PainelFicha.jsx:215` | "Ficha" |
| Resumo: "Pedido e entrega" / "Seleção do atendimento" | modal | "Pedido e entrega · {Nome}" / "Seleção do atendimento · {Nome}" / "Pedido vinculado · {Nome}" | `BlocoPedido` | `PainelFicha.jsx:110,237` | 3 títulos |
| Resumo: "Pagamento" | modal | "Pagamento · {Nome}" | `BlocoCobranca` | `PainelFicha.jsx:268` | "Cobrança" no código |
| Resumo: "Pós-venda" | modal | "Pós-venda · {Nome}" | `BlocoDevolucoes`+`BlocoOcorrencia*` | `PainelFicha.jsx:288` | |
| Resumo: barra inferior, "Andamento" / "Ver etapas" | trilha de 6 passos inline | "Andamento · Etapa n de 6" | `BarraProximoPasso` | `BarraProximoPasso.jsx:498-501` | "esteira" no código |
| Barra: "⋯ Mais ações do pedido" | popover | Cancelar pedido, Desfazer pagamento, Marcar estorno, Voltar etapa, Encerrar atendimento | `MenuDoPedido` | `MenuDoPedido.jsx:41-74` | |
| Resumo: "Ver canhoto" | modal | "Canhoto {n}" | `Canhoto` | `BlocoPedido.jsx:333`, `Canhoto.jsx:86` | "canhoto" |
| Fila de canhotos (pílula global) | impressão | "N canhotos na fila" | `FilaCanhotos` | `FilaCanhotos.jsx:116` | |
| Queda de conexão | modal global | "Lançar o que foi feito no papel" | `ModalLoteDePapel` | `ModalLoteDePapel.jsx:111` | |
| Canto: toasts | pilha | "Contato novo", "Precisa de você", "Mensagem nova", "Sinal pago", "Pagamento confirmado", "Pedido mudou de etapa" | `PilhaDeAvisos` | `dominio/aviso.js:33-51` | |
| Rodapé: faixa | `output` | "Conversas sem atualização…", aviso de ação + "Fechar aviso" | `FaixaApi` | `FaixaApi.jsx` | |

Portas duplicadas para a mesma configuração: **4 caminhos** chegam às mensagens/horários automáticos (modal "Mensagens automáticas", modal "Gestão da loja", modal "Respostas e automáticas", módulo Atendimento > "Respostas e automáticas"/"Horários e mensagens"). **2 lugares** abrem/fecham a loja além do trilho (modal "Mensagens automáticas" > "Abrir/Fechar loja agora", `ModalAutomacoes.jsx:263`; e "Horários e mensagens").

### 1.4 Um mesmo nome para coisas diferentes (e o inverso) no mapa

| Nome na tela | Significados encontrados (FATO) |
|---|---|
| "Balcão" | (a) a tela inteira (`modulos.js:59`, "Voltar ao Balcão"); (b) só a coluna da lista de conversas (`Moldura.jsx:485,524`, "Filtros do Balcão", "Selecione um nome no Balcão"); (c) a aba do celular; (d) `title` do item do trilho que, na tela, diz "Atendimento". **No desktop a palavra nunca é desenhada como rótulo.** |
| "Atendimento" | (a) o módulo; (b) o item do trilho; (c) a coluna do meio e a aba; (d) um ciclo de conversa numerado ("Atendimento 3 encerrado", "Encerrar atendimento", `Thread.jsx:21`) |
| "Resumo" | (a) a coluna da direita (= "Ficha" no código e no texto de ajuda); (b) "Resumo do atendimento" (folha do encerramento, `ModalEncerrar.jsx:158`); (c) "Ver resumo" no histórico (`ModalHistorico.jsx:116`); (d) "Resumo de {Nome}" |
| "Ficha" | aparece **14 vezes** em texto de tela (`Composer.jsx:284`, `ModalEnviarModelo.jsx:91,106`, `ModalProgramarMensagem.jsx:132`, `ModalBloqueio.jsx:32,67,103`, `ComandaDoCliente.jsx:246`, `PainelFicha.jsx:89,91`, `ModalNota.jsx:42`, `resumoDoModo.js:19`, `AjudaDoConsole` via `diagnosticoDoConsole.js:5`) mas **nenhuma coluna se chama Ficha**: a coluna se chama "Resumo" |
| "Avisos" | (a) sino do trilho (notificações internas); (b) toasts ("Avisos do atendimento"); (c) módulo > "Avisos" (configuração); (d) "Avisos do cliente" na ficha (consentimento de canal, `AvisosDoCliente.jsx:104`); (e) "Fechar aviso" da faixa de erro/ação |
| "Lembretes" | relógio no trilho. O sino de "Avisos" e o relógio de "Lembretes" foram separados em #1807 (`Sininho.jsx:275-277`) |
| "Comanda" / "Seleção" / "Pedido" / "Link de compra" / "Link do cliente" / "Cardápio" | o mesmo objeto (o que o cliente quer comprar) tem 6 nomes: "Seleção do atendimento", "Seleção da comanda", "Seleção compartilhada", "Comanda compartilhada com o cliente", "Pedido atual", "Link do cliente" (`ComandaDoCliente.jsx:292,337`; `PainelFicha.jsx:110`; `PainelCardapio.jsx:500,663`) |
| "Automático / automática / Automáticas / Agente / Assistente / IA" | o mesmo "robô" com 6 nomes (ver 4.3) |

---

## 2. Dicionário de ícones

Dois conjuntos convivem no mesmo olhar:
- `Icone` (`componentes/Icone.jsx:9`): **95 chaves**, traço 1,75 em grade 24, estilo Lucide/SF. **3 pares de chaves com o desenho idêntico**: `fechar = x`, `mais = plus`, `lupa = search` (confirmado por comparação do `d`, `dups.mjs`). 247 usos de `<Icone>`.
- `MarcaIcone` (`componentes/MarcaIcone.jsx:7`): **34 chaves**, traço 2 em grade 32, marca V4. 34 usos. `IconeDoModulo` escolhe por módulo/tela entre os dois.
- Misturados na mesma barra: no trilho "Atendimento" e "Ajuda" são `MarcaIcone` (`Moldura.jsx:295,321`), enquanto "Cozinha", "Automáticas", "Gestão", "Entregas", "Avisos", "Lembretes", "Loja" e "Simular" são `Icone`. O mesmo módulo tem ícone diferente no trilho do Balcão e no Hall: Cozinha = `cooking-pot` (trilho) x `pedido-em-preparo` (Hall); Entregas = `moto` x `entrega` (caminhão) (`IconeDoModulo.jsx:5`, `ResumoEntregas.jsx:28`).
- Há também **glifos de texto** em botões: "−", "+", "×" (`ComandaDoCliente.jsx:199-203`, `SugestaoDeTexto.jsx:8`, `BlocoPedido.jsx:63`).
- Sem biblioteca externa (`Icone.jsx:3-5`): nada de Material Symbols no projeto.

### 2.1 Chaves usadas no fluxo do Balcão, significado e equivalente

Contagem = ocorrências da chave no `src` (inclui telas de gestão). "Mat." = nome do Material Symbol convencional.

| Chave | Usos | Onde aparece no Balcão | Significado que carrega | Mat. convencional | Problema |
|---|---|---|---|---|---|
| `inbox` | 2 | Aba "Balcão", título de coluna (`Moldura.jsx:38`, `BarraDeAbas.jsx:6`) | lista de conversas | `inbox` | ok |
| `conversa` | 32 | Módulo Atendimento, aba "Atendimento", ícone padrão de canal (`canal.js:10`), toast "Mensagem nova" | conversa | `chat` | também parece `respostas` (ver abaixo) |
| `ficha` | 8 | Aba/coluna "Resumo", card "Perfil do cliente" | resumo do cliente | `person`/`contact_page`/`badge` | o desenho é um **marcador de página** (bookmark: `M7 3h10…v16l-6-3-6 3V4`, INFERIDO do path): em Gmail/WhatsApp bookmark = salvar/fixar |
| `respostas` | 9 | Botão "Respostas" do composer | respostas prontas | `quickreply` (ou `bolt` no WhatsApp Business) | balão de fala quase igual a `conversa` (`Icone.jsx:11` x `:113`) |
| `arrow-up` | 1 | Botão Enviar texto (`Composer.jsx:292`) | enviar (iMessage) | `send` (WhatsApp/Android: avião de papel) | **o mesmo verbo "Enviar" usa `arrow-up` aqui e `enviar` (avião) em "Enviar áudio", "Enviar link", "Perguntar"** (`ComposerAnexos.jsx:108`, `ModalEnviarCardapio.jsx:66`, `BalaoAssistente.jsx:407`) |
| `enviar` | 6 | Enviar áudio, Enviar link, Perguntar ao assistente | enviar | `send` | ver linha acima |
| `mic` | 2 | Gravar áudio (ocupa o lugar do Enviar com campo vazio, igual WhatsApp) | gravar | `mic` | ok |
| `anexo` | 1 | "Anexar" | clipe | `attach_file` | ok |
| `imagem` | 23 | "Fotos" (composer), "Enviar foto" | foto | `image` | ok |
| `relogio` | 26 | **Lembretes** (trilho), **Programar** mensagem, estado "Esperando você", "Escreveu com a loja fechada", selo "programada", "Ajustar horários da semana", estado "enviando" | tempo | `schedule` (lembrete: `alarm`/`notifications_active`) | **mínimo 7 significados** (`Sininho.jsx:278`, `Composer.jsx:282`, `CartaoConversa.jsx:25-28`, `Balao.jsx:123`, `App.jsx:462`, `BarraProximoPasso.jsx:543`) |
| `bell` | 1 | "Avisos" (`SinoDaSessao.jsx:34`) | notificações | `notifications` | ok, mas Lembretes não usa sino |
| `bell-plus` | 1 | "Programar lembrete" (modal) | | `add_alert` | no painel o "+" é `mais` (`Sininho.jsx:292`) |
| `nota` | 23 | "Nota" (composer), post-it do fio, **prévia de PDF anexado** (`ComposerAnexos.jsx:52`, `BolhaAnexo.jsx:16`, `BolhaMidiaApi.jsx:49`) | nota interna / arquivo | `sticky_note_2` / `description` | uma folha com dobra serve a "Nota interna" e a "arquivo PDF" |
| `modelo` | 5 | "Modelos aprovados", selo "modelo", e **"Usar sugestão no rascunho"** (`Cartoes.jsx:97`) | template | `view_quilt`/`dashboard_customize` | o mesmo glifo significa "template da Meta" e "aplicar sugestão" |
| `agente` | 7 | "Assistente" (gatilho e título), "Sugestão de resposta" | robô/IA | `smart_toy` | dois nomes ("Assistente"/"Sugestão de resposta"/"Agente de atendimento") para um glifo |
| `raio` | 12 | "Automáticas" (trilho/topo), selo "automática", prefixo "Automático" no cartão, estado "Automático", "digitando", **"Reenviar"** (`Balao.jsx:137`) | automático | `bolt` | `raio` = "automático" em 6 lugares e "reenviar" em 1. Reenviar deveria ser `refresh`/`replay` |
| `hand` | 5 | **"Passar para…"** (ação de sair), estado "Com você", motivo "Passou para você", toast "Precisa de você" | assumir/atenção | `back_hand` (transferir: `forward`/`swap_horiz`/`person_add`) | mesma mão para a ação que entrega a conversa e para o estado de quem recebe |
| `log-out` | 14 | **"Encerrar" (cabeçalho, faixa de modo, ⋯ do pedido, barra do pedido), estado "Encerrada", "Sair" da conta, "Fechar o caixa"** (`PainelAtendimento.jsx:149`, `BarraModo.jsx:62`, `MenuDoPedido.jsx:72`, `BarraProximoPasso.jsx:181,359`, `MenuDaConta.jsx:83`, `GestoLoja.jsx:102`) | sair | `logout` (encerrar conversa: `archive`/`check_circle`/`done_all`) | **Encerrar atendimento e Sair da conta têm o mesmo ícone** |
| `archive` | 1 | só em "Arquivar resposta" (`AbaRespostas`) | arquivar | `archive` | existe pronto e não é usado para encerrar conversa |
| `lock` | 7 | **Loja fechada**, **"Só modelo"** (janela fechada), estado **"Bloqueado"** (`Moldura.jsx:182`, `linhaDoBalcao.js:14,58`) | trancado | `lock` | 3 significados; e "Bloqueado" no cabeçalho/composer usa `bloqueio` (círculo cortado, `PainelAtendimento.jsx:140`, `Composer.jsx:160`): **mesmo conceito, dois ícones** |
| `bloqueio` | 8 | cabeçalho "Bloqueado", composer, ficha | bloqueado | `block` | ver `lock` |
| `circle-check` | 13 | Loja aberta, "Pago", "Aprovar pedido", sinal pago | ok/estado | `check_circle` | ok |
| `check` | 49 | mensagem de sistema, **dispensar** card do assistente ("Ok, entendi"), item marcado em "Ordenar", chip ativo, "Confirmar e gerar", "avisada", "Nada pendente", "Enviado", "Corrigir no rascunho", Vazio | confirmar/feito/selecionado | `check` | sentido muda por tela: confirmar, feito, selecionado, descartar |
| `x` / `fechar` | 14 / 11 | fechar modal, limpar busca, remover tag, remover anexo, **tirar prato do cardápio** (`PainelCardapio.jsx:409`) | fechar/remover | `close` | **dois nomes no código para o mesmo desenho**; e "tirar do cardápio" usa `x` enquanto "tirar da galeria" usa `lixeira` (`PainelGaleria.jsx:218`) |
| `lixeira` | 4 | tirar foto da galeria, categoria, receita | excluir | `delete` | ok, mas ver `x` |
| `lapis` | 18 | editar campo/nota/tag | editar | `edit` | ok (existe `editar` em MarcaIcone) |
| `mais` / `plus` | 21 / 21 | adicionar tag, "Nova comanda", mais 1, **"Programar lembrete"** | adicionar | `add` | duas chaves, um desenho |
| `menos` | 10 | menos 1 | | `remove` | ok |
| `search` / `lupa` | 16 / 2 | buscar conversas (`BarraBalcao.jsx:90`), **"Revisão ortográfica"** (`Cartoes.jsx:108`) | buscar | `search` (ortografia: `spellcheck`) | lupa para ortografia |
| `arrow-up-down` | 1 | "Ordenar" | ordenar | `swap_vert` ou `sort` | ok (não há ícone de **filtro**; chips fazem o papel) |
| `ellipsis` | 4 | **"Mais" do topo/celular** (`Moldura.jsx:139`), "Mais ações do cliente", "Mais ações do pedido" | mais opções | `more_vert` (Android, Gmail e WhatsApp usam vertical; iOS usa horizontal) | horizontal onde o padrão pedido é vertical |
| `ajuda` | 4 | "Ajuda" no topo e no `ControleAjuda` (`Moldura.jsx:256`, `App.jsx:453`) | interrogação | `help` | **o trilho usa `MarcaIcone informacao` ("i") para o mesmo "Ajuda"** (`Moldura.jsx:321`) |
| `painel` | 7 | "Gestão" (`Moldura.jsx:237,312`), "Ver módulos" na Gestão | dashboard | `dashboard` | "Gestão" sem engrenagem |
| `automacao` / `configuracoes` (Marca) | 1 / 1 | módulo "Configurações" (sliders) | ajustes | `tune` ou `settings` | **não existe engrenagem** (`settings`) em nenhum dos dois conjuntos |
| `cooking-pot` | 12 | "Cozinha", "Começar preparo", "Pedido atrasado", passo Preparo | cozinha | `skillet`/`soup_kitchen` | ok |
| `package` | 9 | "Marcar pronto", passo Pronto | embalado | `inventory_2` | ok |
| `moto` | 33 | "Saiu para entrega", "Entregas", "entrega atrasada" | entrega | `two_wheeler`/`local_shipping` | ok |
| `house` | 7 | "Marcar entregue", passo Entregue | entregue | `home`/`done_all` | casa como "entregue" é convenção própria |
| `dollar-sign` | 25 | passo Cobrança, "Marcar pago", "Cobrança venceu", "Padrão de compra", "Fechar caixa" | dinheiro | `payments` | "$" é dólar; o app é R$ |
| `circle-x` | 17 | "Cancelar pedido", "Cancelado" | cancelar | `cancel` | ok |
| `undo-2` | 9 | "Desfazer pagamento" **e** "Marcar estorno" (`MenuDoPedido.jsx:53,61`) | desfazer | `undo` | duas ações financeiras diferentes, mesmo ícone |
| `arrow-left` | 3 | "Voltar etapa" | voltar | `arrow_back` | **nenhum modal/tela usa seta de voltar**; "Voltar" é botão de texto (19 botões "Voltar*") |
| `refresh-cw` | 27 | Atualizar, Reenviar cobrança, "Alterar forma de pagamento" | recarregar | `refresh`/`sync` | "Alterar forma" não é recarregar |
| `message-square-warning` | 2 | estado **"Reclamação"** no cartão e faixa **"N mensagens não entregues"** (`CartaoConversa.jsx:23`, `NaoEntregues.jsx:148`) | alerta de mensagem | `report`/`sms_failed` | 2 significados |
| `alerta` | 36 | erros, avisos, "Janela de 24 h", bloqueios | atenção | `warning` | erro e aviso usam o mesmo triângulo |
| `globo`, `whatsapp`, `instagram`, `email` | 2/2/1/6 | selo do canal no avatar e chips de canal | canal | `public`, (marca), `mail` | ok |
| `user-plus` | 4 | "Lead", contato novo, cadastrar | cadastro | `person_add` | ok |
| `termometro` | 1 | "Tom da conversa" | sentimento | `sentiment_satisfied`/`mood` | inventado |
| `dica` | 2 | card CDC | dica | `lightbulb` | ok |
| `sparkles` | 2 | avaliação positiva do cliente (`BolhaAvaliacao.jsx:13`) | IA (convenção) | `auto_awesome` | convenção de "IA" usada para nota do cliente, enquanto a IA usa `agente` |
| `cardapio` | 26 | **"Link de compra"** (`CompartilharComanda.jsx:26`), módulo Cardápio | cardápio | `menu_book` / link: `link`/`share` | o botão compartilhar usa o ícone do módulo; o ícone `link` existe e não é usado aqui |
| `printer`, `download`, `copy`, `external-link` | 12/2/2/3 | imprimir, baixar PDF, copiar diagnóstico, abrir em outra janela | | `print`, `download`, `content_copy`, `open_in_new` | ok |
| `sun` / `moon` | 1 / 1 | `InterruptorTema` | tema | `light_mode`/`dark_mode` | ok |
| `chevron-right/up/down` | 8/6/2 | grupo recolhido, "Ver etapas", acesso da Ficha | expandir/ir | `chevron_right`, `expand_more` | ok |
| `ampliar`, `reduzir`, `trocarLado`, `encaixarEsquerda/Direita`, `solto`, `pega` | 1 cada | painel do cardápio e do assistente | janela | `open_in_full`, `close_fullscreen`, `swap_horiz`, `dock_to_left/right`, `drag_indicator` | inventados; o assistente usa 3 botões de posição, o cardápio usa 1 troca + 1 ampliar: **dois controles diferentes para "mover o painel de lado"** (`BalaoAssistente.jsx:38-42` x `PainelCardapio.jsx:675-690`) |
| `grip-vertical` | 6 | arrastar | | `drag_indicator` | ok |
| `flask-conical` | 3 | "Simular" | laboratório | `science` | ok (demonstração) |
| `thumbs-up/down`, `smile/meh/frown` | 3/3/1 | avaliação | | `thumb_up`, `sentiment_*` | ok |
| `play`, `pause` | 4/4 | áudio; "Ouvir" som; ativar/pausar janela | | `play_arrow`, `pause` | `play` = "Ouvir" e "ativar janela" |
| `estrela`, `presente` | 3/3 | fidelidade | | `star`, `card_giftcard` | ok |

Ausentes que a convenção pede, no Balcão: **engrenagem/configurações** (`settings`), **filtro** (`filter_list`; `MarcaIcone filtrar` existe e não é usado), **fixar** (`push_pin`; "Fixar nota" não tem ícone), **compartilhar** (`share`), **voltar em cabeçalho** (`arrow_back`), **mais opções vertical** (`more_vert`), **arquivar para encerrar** (`archive`), **transferir** (`forward`/`swap_horiz`).

### 2.2 Resumo dos problemas de iconografia (contagens)

| Padrão | Qtde | Exemplos |
|---|---|---|
| Mesmo conceito, ícones diferentes | 8 | Ajuda (`ajuda` x `informacao`); Bloqueado (`lock` x `bloqueio`); Enviar (`arrow-up` x `enviar`); Tirar item (`x` x `lixeira`); Cozinha/Entregas trilho x Hall; Responder (`respostas` x `conversa`); Adicionar (`mais` x `plus` x `adicionar`); Editar (`lapis` x `editar`) |
| Mesmo ícone, sentidos diferentes | 9 | `relogio` (7), `check` (6+), `log-out` (Encerrar x Sair), `hand` (Passar x Com você), `lock` (3), `raio` (Automático x Reenviar), `nota` (Nota x PDF), `modelo` (template x aplicar sugestão), `undo-2` (2 ações), `message-square-warning` (2) |
| Ícone inventado onde há convenção | 7 | `ficha` (bookmark para "Resumo"), `termometro`, `house` para entregue, `cardapio` para compartilhar, `arrow-up` para enviar, `lupa` para ortografia, `ellipsis` horizontal |
| Convenção ausente | 8 | engrenagem, filtro, fixar, compartilhar, voltar, mais vertical, arquivar-encerrar, transferir |
| Chaves duplicadas no código (mesmo `d`) | 3 | `fechar=x`, `mais=plus`, `lupa=search` |

---

## 3. Tooltips e acessibilidade

**Componente de Tooltip: não existe.** `grep` por `tooltip`, `data-tooltip`, `role="tooltip"` e `attr(title)` não acha nada (um `attr(data-rotulo)` em tabela de caixa é outra coisa). Toda dica é o atributo nativo `title=`: **100 no `src` (53 no escopo Balcão)**. Consequências (FATO do código + comportamento padrão do navegador): `title` nativo não aparece com foco de teclado, não existe em toque (tablet e celular, que são alvo declarado: breakpoints em `useTamanhoTela.js`), tem atraso longo e não mostra atalho de forma padronizada.

| Métrica (AST sobre todo o `src`) | Valor |
|---|---|
| Elementos de botão (`button`, `Botao`, `Chip`, label de anexo) | **911** |
| Botões **só com ícone** (sem texto visível) | **54** |
| ... com nome acessível (`aria-label` ou `rotulo` no `Icone`) | **54 de 54 (100%)** |
| ... com `title` (tooltip nativo) | **17 de 54 (31%)** |
| ... sem tooltip nenhum | **37 de 54 (69%)** |
| `aria-label=` no escopo Balcão | 101 |
| Botões de texto que ficam só-ícone por CSS em tela estreita (aparecem sem rótulo): composer `Anexar/Fotos/Programar/Nota` (`@container composer (max-width: 439px)`, `atendimento.module.css:460-463`); topo `Cozinha/Automáticas/Gestão` em < 760 px (`moldura.module.css:190`); pílula "Loja aberta/fechada" (`moldura.module.css:251`) | 8 controles |

Observação: a acessibilidade por leitor de tela está bem coberta (nenhum só-ícone sem nome). A lacuna é de **descoberta visual**: tooltip e rótulo.

### 3.1 Só-ícone SEM `title` (lista completa, 37) 

No escopo do Balcão ou usado por ele (21):

| arquivo:linha | Controle | Nome acessível atual |
|---|---|---|
| `componentes/Chip.jsx:61` | × remover tag | `rotuloRemover` |
| `features/caixa-de-entrada/BarraBalcao.jsx:107` | limpar busca | "Limpar busca" |
| `features/assistente/BalaoAssistente.jsx:312` | fechar assistente | "Fechar" |
| `features/assistente/BalaoAssistente.jsx:403` | enviar pergunta | "Perguntar" |
| `features/atendimento/BolhaAnexo.jsx:57` | tocar/pausar áudio | "Tocar áudio" / "Pausar áudio" |
| `features/atendimento/ComposerAnexos.jsx:57` | remover anexo | "Remover anexo {nome}" |
| `features/anexos/PainelGaleria.jsx:204` | abrir foto | "Abrir foto" |
| `features/ficha-cliente/BlocoPedido.jsx:82,86` | menos/mais 1 | "Menos um" / "Mais um" |
| `features/ficha-cliente/CampoEmLinha.jsx:122,131` | salvar / desfazer edição | "Salvar" / "Desfazer" |
| `features/ficha-cliente/TagsDoCliente.jsx:99` | remover tag | aria |
| `features/hall/HallDeModulos.jsx:75,77` | subir/descer módulo | "Subir/Descer {módulo}" |
| `features/cardapio/PainelCardapio.jsx:186,224,396,404,409,412,693` | menos 1, "Somar", tirar item, editar, fechar cardápio | via `Icone rotulo` |

Fora do Balcão (16): `cardapio-link/TelaCardapioLink.jsx:291,295,387,395`, `cozinha/CartaoCozinha.jsx:80`, `CartaoKds.jsx:61`, `entregas/CartaoEntrega.jsx:89,114`, `PainelViagem.jsx:200,207`, `gestao/caixa/ModalVendaAvulsa.jsx:82,84`, `gestao/cardapio/AbaCardapioApi.jsx:200,201`, `AbaCategoriasApi.jsx:74,76`.

Com `title` mas que NÃO informam: `title="Automáticas"` sobre botão "Automáticas", `title="Gestão"` sobre "Gestão", `title="Módulos"` sobre "Módulos" (`Moldura.jsx:207,276,306,311`): repetem o rótulo visível. Com `title` informativo de qualidade: "Cozinha (abre em outra janela)", "Ajuda e guia do Console", "Ordenar", `Composer` (4 títulos longos de Anexar/Fotos/Programar/Nota).

Dica que só existe no hover (INFERÊNCIA, p. ex. Hall): "A explicação de cada número fica no título do cartão (passar o mouse)" (`PulsoDoHall.jsx:43`). Em tablet/celular a explicação não existe.

---

## 4. Copy: rótulos, títulos, toasts, vazios e erros

### 4.1 Rótulos de botão (FATO, `rotulos.mjs`)

377 botões no escopo Balcão: **314 com texto** (231 rótulos distintos) e 63 só-ícone/sem texto direto (45 são cabeçalhos de aba/bloco com filhos).
- **224 de 314 (71%)** começam com verbo no infinitivo; **90 (29%)** são substantivo, estado ou composto: "Respostas", "Fotos", "Nota", "Tag", "Cozinha", "Gestão", "Ajuda", "Automáticas", "Lembretes", "Avisos (n)", "Assistente", "Todas", "Precisa de você", "Loja aberta/fechada", "Andamento", "Hoje".

### 4.2 Mesma ação, nomes diferentes (INCONSISTENTE)

| Ação | Variantes encontradas (botões) | Onde |
|---|---|---|
| **Dispensar/fechar/voltar** | "Fechar" (19 botões), "Voltar" (20), "Cancelar" (15), "Manter" (11), "Entendi" (4), "Depois", "Continuar comigo", "Continuar trabalhando", "Voltar ao trabalho", "Voltar ao início", "Descartar", "Fechar aviso" (2), "Ok, entendi" (title), "Não ligar", "Manter aberta" | `Modal` tem X próprio ("Fechar janela", `Modal.jsx:98`) **e** quase todo rodapé repete "Fechar" |
| **"Cancelar" com dois sentidos** | descartar a janela ("Cancelar" ×15) x destruir pedido ("Cancelar pedido", "Cancelar e refazer", "Cancelar transporte") | `PainelGaleria.jsx:93` x `MenuDoPedido.jsx:44` |
| **Tentar de novo** | "Tentar novamente" (13), "Tentar de novo" (4: `ImprimirPedido.jsx:229`, `AvisosDoCliente.jsx:82`, `GestoLoja.jsx:217`), "Recarregar esta parte" (`LimiteDeErro.jsx:43`) | |
| **Atualizar** | 11 rótulos: "Atualizar lista/comanda/avisos/lembretes/modelos/devoluções/estornos online/ocorrências/vagas/sistema/agora" | ícone `refresh-cw` ou nenhum |
| **Salvar** | "Salvar", "Salvar cadastro", "Salvar texto", "Salvar horários", "Salvar nota" (histórico), "Salvar seleção", "Salvar ocorrência", "Salvar preferências", **"Fixar nota"** (composer), "Incluir na galeria", "Programar", "Confirmar cadastro", "Aplicar" | "Fixar nota" salva (e a nota passa a ficar fixa no Resumo, `ModalNota.jsx:6-7`), mas é o único "salvar" com outro verbo e não tem ícone de fixar (`push_pin`) |
| **Confirmar** | 19 rótulos "Confirmar *" (alteração, contratação, e enviar, e gerar, encerramento, entregador, frete combinado, registro, reembolso, retorno, troca, agendamento, descarte, devolução, cadastro, solicitação de estorno, e cadastrar, "Confirmar" puro) | |
| **Encerrar** | "Encerrar" (cabeçalho e faixa), "Encerrar atendimento" (barra, menu, 4 lugares), "Encerrar ocorrência", "Encerrar sem estorno", "Confirmar encerramento", "Fechar o caixa" | até 4 botões "Encerrar" ao mesmo tempo (ver 7) |
| **Passar/Transferir** | "Passar para…" (botão), "Passar a conversa de X" (título), "Passando…", "Passou para você" (estado/motivo), "Aguardando equipe" (mesmo estado no chat do site, `CartaoConversa.jsx:41`), "Assumir", "Continuar comigo", "Devolver ao automático" | nunca "Transferir" na tela; o código usa `transferir` (`PassarConversa.jsx:112`) |
| **Enviar** | "Enviar" (aria), "Enviar áudio", "Enviar link", "Enviar modelo", "Enviar foto", "Enviar para…", "Perguntar", "Chamar", "Gerar e enviar", "Confirmar e enviar" | |
| **Cobrança/pagamento** | "Gerar cobrança", "Refazer cobrança", "Reenviar cobrança", "Confirmar e gerar", "Gerar e copiar", "Marcar pago", "Registrar sinal", "Marcar pago (complemento)", "Marcar pago (restante)", "Conferir pagamento", "Conferir comprovante", "Ainda não recebi", "Aceitar diferença", "Cobrar diferença", "Alterar forma de pagamento" | o botão da barra muda de nome por situação (`acaoDoPix.js`, `BarraProximoPasso.jsx:251-255`) |
| **Resposta pronta** | "Respostas" (botão), "Respostas rápidas" (popover), "Respostas prontas" (aria/lista), "Buscar resposta pronta", "Gerenciar respostas", "Respostas e automáticas" (modal e módulo), "Mensagens automáticas" (modal do trilho) | 7 nomes |
| **Abrir cardápio** | "Adicionar itens", "Ver cardápio", "Incluir item novo", "Cardápio de hoje", "Consulta do cardápio", "Link de compra", "Link do cliente", "Combinar entrega desta seleção" | |
| **Ver conversa** | "Abrir {Nome}", "Abrir conversa", "Abrir ficha de {Nome}", "Chamar no WhatsApp" | |
| **Ajuda/Assistente** | "Ajuda", "Assistente", "Sugerir resposta", "Usar no rascunho", "Usar sugestão no rascunho", "Corrigir no rascunho" | |

Contagem: **14 grupos de ação com 2 ou mais nomes**, cobrindo cerca de **165 dos 314 botões com texto**.

### 4.3 Jargão interno ou termo de domínio que vaza para a tela

| Termo | Ocorrências visíveis (escopo) | Onde (arquivo:linha) | Comentário |
|---|---|---|---|
| **Ficha** | 14 | listadas em 1.4 | a coluna se chama "Resumo" |
| **Automático / automática / Automáticas** | 36 strings | `resumoDoModo.js:12`, `Moldura.jsx:225-227`, `Balao.jsx:119`, `BarraModo.jsx:75,84`, `ModalAutomacoes.jsx:242` | mesmo conceito: "Automático" (estado), "automática" (selo da mensagem), "Automáticas" (botão que abre "Mensagens automáticas"), "atendimento automático" (modal) |
| **Agente** | 4 | `PainelAgente.jsx:68` (aria "Agente de atendimento"), `automatico.js:93` ("O agente não responde isso sozinho"), `Balao.jsx:116` ("Escrita pelo agente do EasyStok") | o robô também é "Assistente" (13 strings, `BalaoAssistente.jsx:258,273,353`) e "IA" (`PainelAgente.jsx:94,101`). Ou seja **Automático x Agente x Assistente x IA** para o que o usuário vê como "o robô" |
| **Agora** | `PainelAgora.jsx:61-62` (título do painel do Hall) | | "Agora" + "Agora na casa" + `pedidos até agora` |
| **Esteira** | `Moldura.jsx:64-65` ("N na esteira"), `ModalLoteDePapel.jsx:112` | | o usuário vê "Andamento / Etapa n de 6" |
| **Canhoto** | 8 (`BlocoPedido.jsx:333`, `Canhoto.jsx:27,86`, `FilaCanhotos.jsx:116`, `ModalLoteDePapel`) | | termo operacional da casa, mas sem glossário |
| **Lead** | 3 (`BlocoCliente.jsx:212`, `ModalHistorico.jsx:228`, `FolhaResumo.jsx:103`) | | |
| **Peça** | 15 (`PainelGaleria.jsx:89,171,183,184,237`) | | "Fotos e peças", "Nova peça": o botão do composer se chama "Fotos" |
| **Marcos** | `FolhaResumo.jsx:194` | | |
| **Console** | 7 ("Guia do Console", "com o console minimizado", "este console não conhece", `PilhaDeAvisos.jsx:31`, `BarraProximoPasso.jsx:206`) | | "Console", "sistema", "EasyStok" (4: `BarraProximoPasso.jsx:206`, `Balao.jsx:116`) e "Casa da Baba" para o mesmo produto |
| **Baixa** | `BarraProximoPasso.jsx:206`, "baixa à mão" | | |
| **Modelo / modelo aprovado** | 27 | `Composer.jsx:170`, `ModalEnviarModelo.jsx` | termo da Meta (template) |
| **Sinal / complemento / restante** | 8 | `BarraProximoPasso.jsx:313-318` | |
| **Apurar / Apuração** | 4 | `BlocoOcorrencia.jsx:91`, `BlocoOcorrenciasApi.jsx:68` | |
| **Gesto/Hall/Trilho/Pulso/Modo** | 0 na tela | só código | `Modo de conexão` só em DEV (`PainelAgente.jsx:140`) |
| **Papéis**: "a dona" (13 strings), "gerente", "administrador" (`AvisosDoCliente.jsx:104`), "Operador" (comentários) | 13+ | `App.jsx:576`, `GestoLoja.jsx:142,229,309`, `ModalAutomacoes.jsx:348`, `BlocoDevolucoes.jsx:81`, `BlocoOcorrenciasApi.jsx:66`, `EstornosOnline.jsx:85` | não há vocabulário único de papel |

### 4.4 Rótulos ambíguos (verbo existe, mas não diz o objeto)

"Assumir" (assumir o quê?), "Encerrar" (conversa ou venda?), "Passar para…" (reticências sem o quê), "Nota" (composer; é "Nota interna"), "Programar" (mensagem; o sino também programa), "Respostas" (prontas), "Fotos", "Anexar", "Lançar", "Aplicar", "Remover", "Tag", "Resgatar", "Apurar", "Ligar ao cadastro", "Liberar este pedido", "Virar encomenda agendada", "Recusar com cortesia", "Encaixar e registrar", "Cobrar diferença", "Aceitar diferença" (diferença de quê?). Estimativa: **cerca de 20 rótulos distintos ambíguos**.

### 4.5 Títulos de janela (os que a pessoa lê)

"Mensagens automáticas", "Gestão da loja", "Avisos", "Lembretes", "Ajuda", "Não entregues", "Link de compra", "Passar a conversa de {Nome}", "Resumo do atendimento {n}"/"Encerrar atendimento", "Devolver ao atendimento automático?", "Respostas rápidas", "Respostas e automáticas", "Fotos e peças", "Programar mensagem", "Nota interna · … nunca vê", "Enviar modelo aprovado"/"Chamar no WhatsApp", "Cardápio de hoje"/"Consulta do cardápio", "Cliente e histórico · X", "Pedido e entrega · X"/"Seleção do atendimento · X"/"Pedido vinculado · X", "Pagamento · X", "Pós-venda · X", "Abrir a loja", "Fechar a loja no horário", "Lançar o que foi feito no papel", "Sair sem salvar os ajustes?".

Larguras de modal: **14 valores distintos** passados à mão (`760px` ×4, `min(900px,…)` ×3, `min(760px,…)` ×3, `min(720px,…)` ×3, `720px` ×2, `680px` ×2, `min(980px…)`, `min(880px…)` ×2, `min(740px…)`, `min(600px…)`, `min(520px…)`, `min(480px, 90vw)`): sem escala sm/md/lg.

### 4.6 Toasts e feedback de ação

Existe **1** uso de `<Toast>` (`PilhaDeAvisos.jsx:33`), só para eventos de entrada. Títulos (6): "Contato novo", "Precisa de você", "Mensagem nova", "Sinal pago", "Pagamento confirmado", "Pedido mudou de etapa" (`dominio/aviso.js:33-51`). O resultado de uma ação do usuário **não** usa toast: o feedback é escrito no próprio botão ("Link copiado", "Enviado", "Etapa atualizada.", `ModalEnviarCardapio.jsx:65`, `PainelGaleria.jsx:229`, `BarraProximoPasso.jsx:529`), em `<output>` (**105** no `src`) ou `role="alert"` (**158**), na `FaixaApi` ("Fechar aviso") ou dentro do modal (`AvisoDaAcaoApi`, `Moldura.jsx:346`). **Seis mecanismos de feedback** coexistem: toast, faixa, aviso-no-modal, mensagem inline (`output`/`alert`), texto trocado no botão, "Desfazer · n" (`DesfazerComPrazo`, `BarraProximoPasso.jsx:167`, `TagsDoCliente.jsx:165`).

Não existe `window.confirm/alert` (0 usos): confirmações destrutivas são modais próprios, com o padrão bom "Manter" x verbo claro (`BarraProximoPasso.jsx:596-597`, `BarraModo.jsx:89-90`).

### 4.7 Estados vazios (copy e mecanismo)

| Lugar | Texto | Mecanismo |
|---|---|---|
| Lista, busca sem resultado | `Nada com "{termo}".` + link "Limpar busca" | `VazioEmLinha` (feito à mão) `PainelConversas.jsx:46,131-134` |
| Lista, canal sem espera | "Ninguém esperando você no {canal}." + "Ver todos os canais" | idem `:135-141` |
| Lista, "Precisa de você" vazio | "Ninguém esperando você." + "Ver todas" | idem `:142-144` |
| Lista, filtro de responsável | "Nenhuma conversa sua aberta agora." / "…sem dono aberta agora." + "Ver todas" | idem `:145-147` |
| Lista vazia | "Nenhuma conversa aberta agora." | idem `:148-150` |
| Conversa sem seleção | "Escolha uma conversa" / "Nenhuma conversa para exibir" + "Abrir {Nome}" | `Vazio` `PainelAtendimento.jsx:71-82` |
| Resumo sem seleção | "Sem ficha na tela" + "Abrir ficha de {Nome}" | `Vazio` `PainelFicha.jsx:88-97` |
| Assistente sem conversa | "Nenhuma conversa aberta" | `Vazio` `BalaoAssistente.jsx:320` |
| Painel agente | "Nada pendente nesta conversa." | `<p>` `PainelAgente.jsx:86` |
| Não entregues | "Nenhuma mensagem nesta lista" | `Vazio` |
| Avisos / Lembretes | "Nenhum aviso pendente" / "Nada por agora" | `Vazio` / `<p>` |
| Respostas | "Nenhuma resposta com esse termo." / "Nenhuma resposta pronta cadastrada ainda." | `<p>` `SeletorRespostas.jsx:51` |
| Programadas | "Nenhuma mensagem programada para esta conversa." | `<p>` |
| Cardápio | "Cardápio sem itens" | `Vazio` `PainelCardapio.jsx:740` |
| Galeria | "Nenhum prato com foto" / "Nenhuma peça cadastrada" | `Vazio` |
| Hall / Agora | "Nenhum módulo ou tela encontrado"; "Ninguém esperando você.", "Nada na fila de preparo.", "Nada esperando sair." | `Vazio` / `<p>` |

Duas observações FATO: (a) **"Ninguém esperando você" aparece em 3 telas** (lista, filtro por canal, painel Agora) e **"Precisa de você" é o nome de um filtro, de um estado de cartão, de um toast e de um motivo genérico**; (b) o `Vazio` componente exige título + frase + ação (`Vazio.jsx:5-7`), mas há 5 variantes `VazioEmLinha` e vários `<p>` soltos que não seguem o contrato. Total de `<Vazio>` no escopo: 14.

### 4.8 Erros (amostra)

Mapeados para PT-BR com orientação: `dominio/falhaDeEnvio.js` (erro do canal vira frase), `PainelAgente.jsx:99-104` ("A conexão com a IA está indisponível. Peça ao responsável pelo sistema para verificar."). Sem orientação ou técnicos: `PassarConversa.jsx:155` ("Não foi possível passar a conversa: {erro cru}"), `NaoEntregues.jsx:327` ("A lista não carregou: {e.message}"), `BarraProximoPasso.jsx:205-206` ("um status que este console não conhece"), `Composer.jsx:73,117,118` (primeira pessoa: "Não consegui ler o arquivo", enquanto o resto do app fala "Não foi possível"). Tom: **"Não consegui…" (assistente) x "Não foi possível…" (sistema)**; "Tentar de novo" x "Tentar novamente".

### 4.8b Defeitos de texto e atalhos (FATO)

| Achado | Onde |
|---|---|
| O título do post-it lê "Nota interna · {Cliente} nunca vê": a frase sai com o sentido trocado (quer dizer que o cliente nunca vê) | `ModalNota.jsx:41` |
| Enviar com Enter no composer; **Ctrl+Enter** para perguntar ao assistente (Enter quebra linha) e para salvar a nota; Tab aceita sugestão de texto | `Composer.jsx:217`, `BalaoAssistente.jsx:398-400`, `ModalNota.jsx:50`, `SugestaoDeTexto.jsx:7` |
| Atalhos existentes: F2 (Simular), `/` (respostas), Esc. Nenhum aparece em tooltip padronizado; só no `title` de 2 botões ("Enviar (Enter)", "Simular (F2)") | `Composer.jsx:244,291`, `Moldura.jsx:325` |
| Nome próprio "Thatiane" fixo no modo demonstração (avatar e respostas simuladas) e na classe CSS `perguntaDaThatiane` | `Moldura.jsx:391`, `BalaoAssistente.jsx:358` |

### 4.9 Classificação consolidada dos 231 rótulos distintos de botão (estimativa, regra explicada)

| Classe | Critério | Qtde estimada |
|---|---|---|
| Verbo claro | verbo + objeto explícito ("Gerar cobrança", "Cancelar pedido", "Ver mensagens anteriores") | ~165 |
| Ambíguo | verbo sem objeto, ou nome sem verbo onde caberia ação (4.4) | ~20 |
| Jargão | termo interno/de domínio sem explicação (4.3) | ~12 (Canhoto, Lead, Apurar, Resgatar, Peça, Esteira, Console, Fixar, Automáticas, Devolver ao automático, Baixa, Marcos) |
| Inconsistente | pertence a um dos 14 grupos de 4.2 | ~65 (sobrepõe as demais classes) |

---

## 5. Componentes: primitivos e desvios

### 5.1 O que existe em `componentes/` (FATO)

`Botao` (3 variantes + `perigo` + 2 apelidos legados), `Chip` (papéis filtro/escolha/ação/tag), `Pilula` (tons neutro/ok/aviso/perigo/info/marca), `Modal`, `Popover`, `Toast`, `Vazio`, `Avatar`, `Bloco`, `BarraDeAbas`, `Campo*` (Texto, Area, Selecao, Mascarado, Reais, Dinheiro), `AlcaLargura`, `Anel`, `LimiteDeErro`, `MenuDaConta`, `InterruptorTema`, `ListaDados`, `DesfazerComPrazo`. **Faltam**: `BotaoIcone` (os 54 só-ícone são `<button>` crus ou `Botao` com `aria-label` repetido), `Tooltip`, `ItemDeMenu`, `Selo/Badge/Contador`, `PontoDeAtencao`, `Cartao`, escala de largura para `Modal`.

### 5.2 Uso no escopo Balcão

| Primitivo | Usos | Equivalente feito à mão | Usos à mão |
|---|---|---|---|
| `Botao` | 264 | `<button>` cru | **58** (+ 127 no `src` inteiro) |
| `Chip` | **4** | 2 famílias de chip à mão: filtro/canal (`.chipFiltro`, `.chipCanal`, `BarraBalcao.jsx:162,170,237`) e tag (`.chipTexto/.chipTracejado/.chipSugestao`, `TagsDoCliente.jsx:96,126,140,152`) | 2 famílias (~9 botões) |
| `Pilula` | 24 | `selo`/`estado_*`/`naoLidas`/`conta`/`contaTrilho`/`seloTrilho`/`badge`/`ponto*` | **~25 classes de selo/ponto/contador** (lista abaixo) |
| `Modal` | 36 | `ModalNota` (`<dialog open>`), `BalaoAssistente`, painel `Sininho`, `PainelCardapio`, `MenuDaConta`, `Gaveta` | 5 overlays próprios |
| `Vazio` | 14 | `VazioEmLinha` + `<p>` | 5 + ~15 |
| `Campo*` | 61 | `<input>` cru 21, `<select>` 4, `<textarea>` 2 | 27 (`BarraBalcao` busca e Responsável, `SeletorRespostas`, `TagsDoCliente`, `ComandaDoCliente`) |
| `Toast` | 1 | `FaixaApi`, `AvisoDaAcaoApi`, `output`/`alert` | 6 mecanismos |
| `Popover` | 6 | `MenuDaConta` (outra implementação), menus de `⋯` | 2 |

Desvios mais repetidos, por severidade:
1. **Chips de filtro do Balcão ignoram `Chip`** (que tem o papel `filtro` com `aria-pressed`, exatamente o que `BarraBalcao` reimplementa) e **chips de tag ignoram `Chip papel="tag"`**.
2. **Selo/contador/ponto hand-made**: `.contaTrilho`, `.seloTrilho`, `.conta` (Moldura), `.conta` (MenuDaConta), `.badge` (Sininho), `.pontoAtencao` (assistente), `.pontoNaoVisto`, `.pontoPrecisa`, `.pontoOrdem`, `.numeroChip`, `.naoLidas`, `.estado_*` (4), `.selo`/`.seloBotao` (fio), `.seloCaptado`/`.seloInformado`/`.seloPontos` (cliente), `.contagemEtapa`, `.marcaEstorno`, `.contador` (3 arquivos), `.pontoAparelho`. Total ~25 classes.
3. **Botão só-ícone cru** (54), cada um com seu CSS: `.limparBusca`, `.previaRemover`, `.botaoPosicao`, `.fechar`, `.fecharCanto`…
4. **Overlay próprio** (5): `ModalNota` é um `<dialog open>` não modal de propósito (post-it, `ModalNota.jsx:5-7`), `BalaoAssistente`, painel do `Sininho`, `PainelCardapio`, `MenuDaConta`; cada um implementa o próprio Esc, foco e posição.
5. **Hierarquia de `Botao`**: 745 usos; variante explícita: `texto` 227, `primario` 158, `secundario` 59, `perigo` 4, legado `padrao/discreto` 4; sem variante (secundário implícito) ~294. Ações de texto simples (227) têm pouca affordance.
6. **Botão primário competindo**: até 4 `primario` visíveis juntos no desktop (Assumir, Enviar, Adicionar itens/Cadastrar cliente, ação da barra de andamento).
7. **Duas fontes de "Link de compra"** na mesma tela (ver 7).

### 5.3 Tokens e CSS (FATO, `tokens.mjs`)

O CSS é **disciplinado**: 0 cores hex fora de `tokens.css`; `font-size` por token 328 x 2 literais no escopo Balcão; espaçamento/raio quase todos por variável. Desvios restantes: **28 `border-radius` em px** no escopo Balcão (≈11 são `999px` em vez de `--r-pilula`; o resto usa 8, 12, 14, 18, 20, 24 px, fora da escala 10/16/22), `rgba()` literal em 11 pontos (sombras e brilhos), `min-height: 44px` ×16 e `width: 20px` ×6 literais (toque), **`z-index` literal ×17**, `!important` ×9. Tamanho de toque: `--toque: 40px` (44 em tela de toque, `tokens.css:124,178`), mas existem controles com `min-height: 36px` (×6) e ícones de `20px`/`18px` clicáveis.

---

## 6. Movimento

### 6.1 Tokens (existem, `estilos/tokens.css`)

Durações: `--dur-micro 90ms`, `--dur-curta 150ms`, `--dur-media 220ms`, `--dur-longa 320ms`. Easings: `--ease-padrao`, `--ease-saida` (entrada), `--ease-entrada` (saída), `--ease-pulo`, `--ease-mola` (`linear()`). Regra comentada: "saída sempre mais curta que a entrada".

### 6.2 Uso (FATO, `movimento.mjs`, 90 arquivos CSS)

| Medida | Valor |
|---|---|
| `transition:` declarações | 44: **26 só com token**, 17 com valor literal, 1 mista |
| `animation:` declarações | 59: **24 só com token**, 23 literais, 12 mistas |
| `@keyframes` | 51 |
| Uso dos tokens | `dur-curta` 30, `dur-media` 23, `dur-micro` 12, `dur-longa` 9; `ease-padrao` 47, `ease-saida` 19, `ease-mola` 10, `ease-pulo` 8, `ease-entrada` **1** |
| Durações literais fora da escala | 180ms ×12 (anexos, assistente, bolha, composer), 200/220/240/300/400/600/650/800/1000/1100/1200/1400/1500/1600/2000/2400 ms |
| Easings literais | `ease` ×18, `linear` ×4, `ease-in-out` ×3, `ease-out` ×2 |
| Propriedades em `transition` | background 14, transform 10, background-color 9, border-color 9, box-shadow 8, color 7, height 2, width 2, opacity 2 |
| Animações infinitas | **8**: `seloPulso` 2,4 s (`moldura.module.css:171`), `piscaSla` 1,6 s (cartão com prazo estourado, `caixa.module.css:49`), `passoAgoraPulso` 1,4 s (passo atual da trilha, `ficha.module.css:482`), `pontoDigitando` 1,1 s, `girarMeta` .8 s, `girar` .8 s, `pulsarVivo` 2 s (cozinha), `piscaCheio` 2,4 s (entregas) |
| Transição global de `button` | `base.css:43-56`: fundo/borda/cor/sombra/opacidade em 150 ms + `transform` em 220 ms com mola; `:active` afunda `scale(.975)` em 90 ms |
| Entrada de camada | Modal `entrar` 220 ms + `cortina` 220 ms, folha `subirFolha` no celular (`Modal.module.css:17-21,79`); Popover `abrir` 150 ms; Toast `entrarAviso` 220 ms; cartão da lista `entradaCartao` 220 ms a cada montagem (`caixa.module.css:19`); balão `entradaBalao` |
| Saída de camada | **só a Cozinha tem keyframe de saída** (`cartaoSair`). Modal, Popover e Toast somem sem animação de saída (FATO pelos `@keyframes`). |
| Troca de rota | sem transição (a hash troca a árvore) |
| Tema | `document.startViewTransition` (`hooks/useTema.js:16-17`), desligado se movimento reduzido; `base.css:80-84` |

### 6.3 `prefers-reduced-motion`

Existe, e é **global**: `base.css:115-123` zera duração de `*`, força `animation-iteration-count: 1` e desliga `view-transition`. Mais **17** blocos locais em 14 arquivos (`MenuDaConta`, `ModalFrete`, `assistente`, `atendimento` ×2, `caixa` ×3, `cardapio`, `cozinha`, `cliente`, `ficha`, `RelogioPix`, `producao` ×2, `hall`). O Hall tem preferência própria "Animações suaves" ("a preferência de reduzir movimentos do seu aparelho tem prioridade", `PersonalizarHall.jsx`). JS: `scrollIntoView` respeita a preferência em 3 pontos (`App.jsx:202`, `PainelCardapio.jsx:605`, `AbaAcervo.jsx:89`); os outros 9 chamam sem `behavior`.

SUSPEITA: `entradaCartao` toca para cada cartão ao remontar a lista (troca de filtro/ordem pode "piscar" 8-12 cartões); precisa de teste em tela.

---

## 7. Densidade por tela do Balcão (estimativa pelo JSX; estado típico)

Controles interativos visíveis ao mesmo tempo, desktop 3 colunas, WhatsApp, pedido criado aguardando pagamento:

| Região | Controles | Detalhe (arquivo:linha) |
|---|---|---|
| Trilho | **10** (+1 Simular na demo; +2 escondidos atrás do avatar: Tema, Sair) | Módulos, Loja, Entregas, Cozinha, Automáticas, Gestão, Avisos, Lembretes, Ajuda, Avatar (`Moldura.jsx:276-336`) |
| Lista de conversas (topo) | **8-9** | busca, limpar, Ordenar, chip Todas, chip Precisa de você, 1-3 chips de canal, select Responsável (`BarraBalcao.jsx:87-170`) |
| Lista (corpo) | **N cartões (8-12 por tela)** + faixa "Não entregues" (0-1) + 2 grupos recolhidos (Encerradas, Bloqueados) | `CartaoConversa`, `PainelConversas.jsx:166-193` |
| Conversa, cabeçalho | **4** | Link de compra, Passar para…, Encerrar, Assistente (`PainelAtendimento.jsx:134,147,149`, `BalaoAssistente.jsx:246`) |
| Conversa, faixa de modo | **1-3** | Assumir; Encerrar (às vezes); Devolver ao automático (`BarraModo.jsx:56-77`) |
| Conversa, fio | 0 a ~6 | selos "automática" são botões (`Balao.jsx:113`), "Reenviar", "Modelos aprovados", "Ver mensagens anteriores", play de áudio |
| Composer | **7** | Respostas, Anexar, Fotos, Programar, Nota, Enviar/Gravar, campo (`Composer.jsx:238-306`) |
| Resumo | **9-14** | cartão do cliente, "Link do cliente" + **outro "Link de compra"**, Adicionar itens / Nova comanda / Cadastrar cliente, 3-4 acessos (Pedido e entrega, Pagamento, Cliente e histórico, Pós-venda), Andamento (expandir), ação da barra (1-3), ⋯ (`PainelFicha.jsx:157-212`, `ComandaDoCliente.jsx:292`, `BarraProximoPasso.jsx:498-517`) |
| Flutuantes | 1 (gatilho do assistente, portado para o cabeçalho; sem âncora fica flutuando) + toasts 0-4 | |
| **Total aproximado** | **50 a 70 controles** (média ~60), contando 10 cartões na lista | |

Teto estático por arquivo (JSX): `ComandaDoCliente` 29, `BarraProximoPasso` 27, `PainelCardapio` 24, `BlocoCobranca` 20, `Moldura` 20, `Composer` 10 (mais `ModalAutomacoes` 14, `GestoLoja` 14).

Celular: 3 abas (uma por vez): Balcão ≈ 9 + N cartões; Atendimento ≈ 22; Resumo ≈ 12; topo ≈ 5 mais menu ⋯.

### 7.1 Fontes de ruído mais fortes (ordenadas)

| # | Ruído | Medida |
|---|---|---|
| 1 | **"Encerrar" repetido**: cabeçalho (`PainelAtendimento.jsx:149`), faixa de modo (`BarraModo.jsx:62`), menu ⋯ do pedido (`MenuDoPedido.jsx:72`), barra de andamento (`BarraProximoPasso.jsx:181,359`); todos com o mesmo ícone de "Sair" | 2 a 4 ao mesmo tempo |
| 2 | **"Link de compra" duplicado** na mesma tela: `PainelAtendimento.jsx:134` e `ComandaDoCliente.jsx:292` (via `acaoCompartilhar`, `Moldura.jsx:406,507`) | 2 botões idênticos |
| 3 | **Mensagens/horários automáticos com 4 portas** (ver 1.3) | 4 |
| 4 | **6 mecanismos de feedback** (4.6) | 6 |
| 5 | **~25 classes de selo/ponto/contador** hand-made | ~25 |
| 6 | **Badges numéricos por toda parte**: contador de Automáticas, de Avisos, de Lembretes, de Entregas (trilho), número em cada chip (5), "n não lidas" em cada cartão, "Etapa n de 6", "Ver mensagens anteriores" | ≥ 10 números simultâneos |
| 7 | **Cartão de conversa** com até 5 camadas: avatar com selo do canal, nome + comanda, hora, prévia + prefixo (raio/Você/Sistema), badge de não lidas, ponto de pagamento novo, etiqueta de estado (ícone+texto), contexto (origem, pedido, "entrega até", "Só modelo", tag), trilho lateral, pisca de SLA (`CartaoConversa.jsx:98-165`) | 8+ elementos por cartão, ×10 cartões |
| 8 | **Cores de status**: famílias semânticas ok (verde), aviso (âmbar), perigo (vermelho), info (azul), marca (laranja), entregue (índigo), neutro (6 + neutro) **mais** 5-6 cores de canal (WhatsApp verde, Instagram rosa, site marrom, e-mail roxo, SMS cinza, Messenger azul) | 12 cores com significado na mesma tela; verde de "ok/Pago/Loja aberta" é quase o verde do WhatsApp (`#2B6A45` x `#1A7A43`, `tokens.css`) |
| 9 | **Barra de andamento**: 6 círculos com ícone distinto + rótulo curto + "Etapa n de 6" + "Próximo: X" + botão + ⋯ | 6 ícones |
| 10 | **Composer** com 5 botões rotulados + 1 de ação + campo; dica "Só texto neste canal"; "Mensagens prontas aprovadas pelo WhatsApp…" (frase longa) | |
| 11 | **Trilho** com 10 destinos de natureza diferente (navegação, estado da loja, ferramenta, ajuda, conta) no mesmo nível | 10 |
| 12 | **Ficha** mostra a mesma informação em 3 camadas: seção "Pedido criado/Comanda 12" + cartão do cliente + acessos que repetem resumo ("Falta R$ x" aparece no resumo, no acesso Pagamento, na barra) | |

---

## 8. Os 15 achados mais fortes (priorizados)

| # | Achado | Evidência |
|---|---|---|
| 1 | **No desktop nenhuma coluna tem título visível**; "Balcão" nunca é desenhado como rótulo, só o trilho diz "Atendimento". O dono procura "Balcão" e não o encontra | `Moldura.jsx:485,506` (`semTitulo`), `:296` |
| 2 | **"Balcão" = 4 coisas e "Resumo" = 4 coisas e "Ficha" (14 textos) não existe como rótulo** | 1.4 |
| 3 | **O mesmo "robô" tem 6 nomes**: Automático, automática, Automáticas, Agente, Assistente, IA | `resumoDoModo.js:12`, `Moldura.jsx:225`, `BalaoAssistente.jsx:273`, `PainelAgente.jsx:68,94`, `automatico.js:93` |
| 4 | **Ícone de Encerrar = ícone de Sair da conta** (`log-out`), e há 2 a 4 "Encerrar" juntos | `Icone.jsx:63`; 7.1 #1 |
| 5 | **Zero tooltip de verdade**: sem componente; `title` nativo (100), 69% dos 54 botões só-ícone sem dica, nada em toque/foco | seção 3 |
| 6 | **Não existe engrenagem de configurações**; "Configurações" usa sliders e "Gestão" usa dashboard; as configs do Balcão têm 4 portas | `modulos.js:99`, `Icone.jsx:25`, 1.3 |
| 7 | **`relogio` com 7 sentidos** (Lembretes, Programar, Esperando você, Loja fechada, horários, programada, enviando) e `check` com 6+ | seção 2.1 |
| 8 | **"Enviar" tem dois ícones no mesmo composer** (`arrow-up` x avião) e "Reenviar" usa o raio de "automático" | `Composer.jsx:292`, `ComposerAnexos.jsx:108`, `Balao.jsx:137` |
| 9 | **Dois conjuntos de ícone misturados** no trilho, e o mesmo módulo com ícone diferente no Balcão e no Hall (Cozinha, Entregas) | `Moldura.jsx:295-321`, `IconeDoModulo.jsx` |
| 10 | **14 grupos de ação com nomes diferentes** (Fechar/Voltar/Cancelar/Manter/Entendi; Tentar novamente/de novo; Salvar x Fixar; Atualizar ×11; Confirmar ×19; Passar/Assumir/Devolver) | 4.2 |
| 11 | **"Link de compra" aparece duas vezes** (cabeçalho e Resumo) e usa o ícone do módulo Cardápio | `PainelAtendimento.jsx:134`, `ComandaDoCliente.jsx:292`, `CompartilharComanda.jsx:26` |
| 12 | **Cesta do cliente com 6 nomes** (Seleção do atendimento, Seleção da comanda, Comanda compartilhada, Pedido atual, Link do cliente, Cardápio de hoje/Consulta) | `ComandaDoCliente.jsx:292,337`, `PainelFicha.jsx:110`, `PainelCardapio.jsx:663` |
| 13 | **Primitivos existem e o Balcão não os usa onde deveria**: `Chip` 4 usos x ~12 chips à mão; `Pilula` 24 x ~25 selos à mão; 58 `<button>` crus; 5 overlays próprios; `Modal` com 14 larguras | 5.2 |
| 14 | **Movimento tem tokens, mas 17 de 44 transições e 35 de 59 animações usam valor literal** (`180ms` ×12, `ease` ×18); não há saída animada de Modal/Popover/Toast; 8 animações infinitas (SLA, passo atual, selo). `prefers-reduced-motion` global OK | 6 |
| 15 | **~60 controles visíveis ao mesmo tempo no desktop** e feedback por 6 mecanismos; cartão de conversa com 8+ elementos; 12 cores com significado na mesma tela | seção 7 |

## 9. Pontos fracos deste inventário

- Nenhuma tela foi renderizada: densidade, quebra de linha, "tooltip existe no hover", comportamento em 390/768/1280 e contraste real são INFERÊNCIA a confirmar na tela real (a banca de UX da memória do projeto exige medir no modo API em 3 larguras).
- O parser contou "só-ícone" como botão sem texto estático; botões cujo texto é `{variável}` entram como "com texto". O número 54 é, portanto, piso.
- "Classe de copy" (4.9) é julgamento meu; as contagens de 4.1 a 4.3 e 5.2 são mecânicas e reproduzíveis com os scripts de `scratchpad\banca\`.
- Não li `gestao/*`, `entregas/*`, `cozinha/*`, `cardapio-link/*` linha a linha (fora do Balcão): servem só de contexto para totais globais.
