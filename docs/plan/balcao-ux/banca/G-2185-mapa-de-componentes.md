BANCA: UM COMPONENTE POR CONCEITO, tela de Atendimento (console React)

Base dos caminhos: C:\rep\EasyStok\.claude\worktrees\harmonia-atendimento\EasyStock.Console\src (abaixo `app/...` = `src/app/...`).
Método: leitura estática + scripts de contagem (nada executado, nenhuma tela medida). Scripts em C:\Users\felip\AppData\Local\Temp\claude\C--rep-EasyStok\74df33a2-a382-4f25-a2c0-da3416bd990f\scratchpad\ (orfaos.mjs, precisa.mjs, mortas.mjs, harmonia.mjs, grade.mjs, icones.mjs, tokens.mjs, duplicadas.mjs, iconeso.mjs).
Nada foi editado, nenhum servidor subido, nenhum processo encerrado.

VEREDITO EM NÚMEROS (todos contados no código)
- Menu de overflow: 7 implementações, 5 rótulos, 3 ícones, 3 tamanhos de ícone. É a queixa "ora mais opções está escrito, ora tem ...".
- Botão só de ícone: `BotaoIcone` tem 6 usos nas telas; ≥8 `Botao` só-ícone e ≈12 `<button>` crus fazem o mesmo gesto.
- ≈55 `<button>` crus em 25 arquivos; 17 `<details>`; 53 `role="alert"` com ≥20 classes de erro diferentes; 47 `title=` nativos contra 8 `<Tooltip>`.
- Ícones: 11 tamanhos explícitos (12,13,14,15,16,18,20,22,24,32,40) + AvatarAssistente 28/32/36 + 64 `<Icone>` sem tamanho (1.15em, cresce com a fonte do pai).
- Fontes: 13/14/15 px disputam o mesmo papel (rótulo de controle). `--t-minimo` e `--t-micro` são o mesmo valor com 2 nomes (71 usos).
- Raios: 5 tokens, TODOS 0px (`estilos/tokens.css:135-139`); 12 valores literais (50%, 999px, 8px, 2px) furam o sistema.
- Espaço: 85 literais fora da grade de 4 px (gap 36 de 314, padding 39 de 327, margin 10 de 337).
- `componentes/Selo.jsx`: 0 usos nas telas. 3 referências quebradas (2 classes + 1 token). ≈37 classes CSS órfãs, 5 `modo_*` sem efeito, 4 blocos de regras que não casam mais.

====================================================================
1. MAPA DE COMPONENTES
====================================================================
| Região | Componente | Arquivo:linha | Renderiza | Base que usa |
|---|---|---|---|---|
| Moldura | `Moldura`/`ConteudoMoldura` | app/Moldura.jsx:437,441 | 3 arranjos: desktop 3 colunas + trilho; tablet topo + gaveta; celular topo + abas | AlcaLargura, BarraDeAbas, LimiteDeErro, Botao, BotaoIcone |
| Trilho (desktop) | `Trilho` | app/Moldura.jsx:307 + componentes/Trilho.module.css | marca, Loja, Atendimento, Entregas, Cozinha, Automáticas, Gestão, Avisos, Lembretes, Ajuda, Disposição, Simular, conta | Botao(texto)+`.itemTrilho`, MenuDaConta, InterruptorTema, Sininho, Popover (Disposição, :291) |
| Topo (tablet/celular) | `Cabecalho`+`MenuMais`+`ControleLoja` | app/Moldura.jsx:210, 79, 186 | marca, tema, Loja, Sininho, Cozinha, Automáticas, Gestão, Ajuda, Mais | Botao, Pilula(+icone), Popover, MenuDaConta |
| Colunas | `Coluna` | app/Moldura.jsx:46 | casca da coluna. O título NUNCA aparece (3 chamadas com `semTitulo`: :562,:583,:599) | Icone |
| Gaveta (tablet) | `Gaveta` | app/Moldura.jsx:381 | `<dialog open>` não modal com a Ficha | dialog próprio |
| Lista, barra | `BarraBalcao`+`ChipCanal` | caixa-de-entrada/BarraBalcao.jsx:57,35 | h2 "Conversas N", filtro (list-filter), busca, chips Todas/Precisa | Botao, Icone, Popover; busca e chips CRUS |
| Lista, cartão | `CartaoConversa` | caixa-de-entrada/CartaoConversa.jsx:58 | `<button>`: avatar+canal, nome, hora, prévia, contador, estado/contexto | Avatar, Icone, AssuntoEmail |
| Lista, apoio | `GrupoRecolhido`, `VazioEmLinha`, `NaoEntregues`, `ChamarClientes` | caixa-de-entrada/PainelConversas.jsx:15,48; NaoEntregues.jsx:47; atendimento/ChamarClientes.jsx:41 | grupos Encerradas/Bloqueados, vazio em linha, faixa de falhas + modal | Modal, Vazio, Pilula, Botao |
| Cabeçalho da conversa | `PainelAtendimento` (header inline) | atendimento/PainelAtendimento.jsx:131-179 | avatar, nome, canal, prazo, Bloqueado, Assistente (portal), menu `<details>` | Avatar, Pilula, Botao, Icone |
| Barra de modo | `BarraModo` | atendimento/BarraModo.jsx:24 | Pilula do modo, frase, Assumir, "Mais ações" (`<details>`), Modal Devolver | Pilula, Botao, Modal |
| Fio | `Thread`; internos `Divisoria`:18, `SeparadorDeDia`:30, `Digitando`:43, `PostIt`:57 | atendimento/Thread.jsx:68 | log de bolhas, dias, notas, divisória de atendimento | Botao, Pilula, AssuntoEmail, Icone |
| Bolha | `Balao` | atendimento/Balao.jsx:68 | texto/mídia, selos (automática, modelo, programada), tique, Reenviar | Icone + 3 `<button>` crus (:113,:129,:136) |
| Composer | `Composer` | atendimento/Composer.jsx:31 | aviso de janela, campo, Respostas, Nota, "+" (`<details>`), Enviar/mic | Botao, BotaoIcone, Tooltip, Campo, SugestaoDeTexto, Icone |
| Composer, anexos | `BotaoAnexarArquivo`:25, `PreviaAnexo`:52, `GravadorAudio`:74 | atendimento/ComposerAnexos.jsx | Anexar (label), prévia, gravador | Botao, BotaoIcone, Tooltip |
| Respostas | `SeletorRespostas` | atendimento/SeletorRespostas.jsx:10 | listbox com busca e "Gerenciar respostas" | Popover(portal) + `<input>` e `<button>` crus |
| Ficha | `PainelFicha`/`FichaDoAtendimento` | ficha-cliente/PainelFicha.jsx:48,88 | cabeçalho "Ficha", CartaoDoCliente, resumo "Comanda N", 5 `AcessoDaFicha`, notas, "Link do cliente", IA, BarraProximoPasso | Botao, BotaoIcone, Icone, Modal, PainelMovel, Vazio |
| Ficha, cliente | `CartaoDoCliente` | ficha-cliente/CartaoDoCliente.jsx:15 | botão que abre cadastro, avatar, tags, Frete | Avatar, Botao, Icone |
| Ficha, passo | `BarraProximoPasso`+`MenuDoPedido` | ficha-cliente/BarraProximoPasso.jsx:64; MenuDoPedido.jsx | trilha de 6 passos, ação primária, ⋮, confirmações em linha | Botao, Campo, CampoReais, Popover, Icone |
| Comanda | `BlocoPedido` (+BlocoEntrega, BlocoFreteApi) dentro de `PainelMovel` | ficha-cliente/BlocoPedido.jsx:111; PainelFicha.jsx:311 | itens, quantidade, janela, total | Botao, Campo, Pilula, PainelMovel |
| Comanda do cliente (link) | `ComandaDoCliente` | ficha-cliente/ComandaDoCliente.jsx:321 | seleção do cliente, 8 `<details>` | Botao, Modal, SugestaoDeTexto |
| Cadastro | `ModalCliente`+`BlocoCliente`+`ModalHistorico` | ficha-cliente/ModalCliente.jsx:14; BlocoCliente.jsx:77; ModalHistorico.jsx:208 | modal titulada "Ficha" | Modal, Bloco, Popover, BarraDeAbas, Pilula, Vazio, Avatar |
| Notas | `NotasDoCliente`, `MesaDeNotas`, `ModalNota` | ficha-cliente/NotasDoCliente.jsx:52; MesaDeNotas.jsx:154; notas/ModalNota.jsx:13 | lista, notas soltas, post-it (é `<dialog open>`, não Modal) | PainelMovel, Botao, Campo |
| Pagamento / Pós-venda | `Modal` inline + `BlocoCobranca` | ficha-cliente/PainelFicha.jsx:350,370; BlocoCobranca.jsx:265 | cobrança Pix, devoluções, ocorrências | Modal, Bloco, Campo, CampoReais, Pilula |
| Modais da conversa | ModalEnviarCardapio, ModalEnviarModelo, ModalProgramarMensagem, PassarConversa | atendimento/*.jsx | cardápio, modelo aprovado, programar, passar | Modal, Botao, Campo, Pilula |
| Encerramento | `ModalEncerrar`, `FolhaResumo` | encerramento/ModalEncerrar.jsx:33 | modal "Concluir conversa" + folha | Modal, Botao, Campo, Chip, Pilula |
| Assistente | `BalaoAssistente`, `Cartoes`, `AcoesPropostas` | assistente/BalaoAssistente.jsx:47 | gatilho (portal no cabeçalho), painel flutuante, cartões | Botao, AlcaLargura, AvatarAssistente, Campo, Vazio, Chip, Pilula |
| Lembretes | `Sininho` | lembretes/Sininho.jsx:99 | botão relógio + painel próprio | Botao, Icone (+Modal em ModalProgramarLembrete) |
| Avisos | `PilhaDeAvisos`→`Toast`; `ProvedorDeAviso`; `FaixaApi`/`AvisoDaAcaoApi` | app/PilhaDeAvisos.jsx; componentes/Toast.jsx, Aviso.jsx; app/FaixaApi.jsx | pilha topo-direita; snackbar no pé; faixa; saída em `<output>` | Botao, BotaoIcone |

====================================================================
2. INVENTÁRIO DE CONCEITOS REPETIDOS
====================================================================

2.1 MENU DE OVERFLOW (7 implementações)
| # | Onde | Gatilho | Rótulo | Ícone | Menu |
|---|---|---|---|---|---|
| 1 | app/Moldura.jsx:134-158 | `Botao` secundário | "Mais" (:139) | ellipsis-vertical 20 (:142) | Popover abaixo+portal, `grupos` |
| 2 | atendimento/PainelAtendimento.jsx:152-177 | `<summary>` | "Opções da conversa" (:158) | ellipsis, sem tamanho (≈17px) | `<details>`+`.menuConversa` (atendimento.module.css:35-47) |
| 3 | atendimento/BarraModo.jsx:70-89 | `<summary>` TEXTO | "Mais ações" (:75) | nenhum | `<details>`+`.listaAcoesModo` (css:252-257) |
| 4 | atendimento/Composer.jsx:332-390 | `<summary>` | "Mais ferramentas" (:338) | `mais` (sinal +), sem tamanho | `<details>`+`.menuFerramentas` (css:35-48) |
| 5 | ficha-cliente/MenuDoPedido.jsx:29-52 | `Botao` secundário `.gatilhoMenu` | "Mais ações do pedido" (:34) | ellipsis-vertical 24 (:38) | Popover abaixo+portal, itens crus `.itemMenu` (ficha.module.css:122) |
| 6 | ficha-cliente/BlocoCliente.jsx:225-263 | idem | "Mais ações do cliente" (:229) | ellipsis-vertical 24 (:233) | Popover abaixo SEM portal |
| 7 | caixa-de-entrada/BarraBalcao.jsx:93-120 | `Botao` texto `.botaoOrdenar` | "Filtrar e ordenar conversas" | list-filter 20 (:97) | Popover `dialog` abaixo |
Outros que repetem Esc/clique-fora/posição na mão: Sininho (Sininho.jsx:~120-170 + painel `.painel` lembretes.module.css:30), MenuDaConta (componentes/MenuDaConta.jsx:40-64), Gaveta (Moldura.jsx:381).
Itens de menu com 6 desenhos: Popover `.item` (strong 15px + span 14px, Popover.module.css:23-30), `.itemMenu` (14px, ficha.module.css:122), `.itemOrdem` (caixa.module.css:180), `.menuConversa button`, `.listaAcoesModo > button`, `.menuFerramentas button` (atendimento.module.css:42-47). Larguras de menu: 260/280/300/360/400. Sombra: `--sombra` pequena nos 3 `<details>` (atendimento.module.css:38,255; lembretes.module.css:38) contra `--sombra-media` no Popover (Popover.module.css:9).
ÚNICA: `Popover` + um gatilho `BotaoIcone` (ellipsis-vertical 20, Tooltip "Mais ações").
FALTA no Popover: (a) componente de gatilho com estado/aria/retorno de foco (hoje cada chamador recria `useState`+`Botao`); (b) item com `icone`, `disabled`+motivo, `tom=perigo`, `aoEscolher` por item, separador; (c) item "filho livre" estilizado (CompartilharComanda e PassarConversa renderizam Botao+Modal e hoje dependem de `.menuConversa button` para parecer item); (d) `portal` como padrão (BlocoCliente:236 esqueceu); (e) largura única.

2.2 BOTÃO DE ÍCONE
| Forma | Onde |
|---|---|
| `BotaoIcone` (certo) | Moldura.jsx:606; Composer.jsx:408; ComposerAnexos.jsx:62; AreaColaborativaIa.jsx:44; BotaoBaixarFicha.jsx:23; PainelFicha.jsx:223 (+Aviso.jsx:91) |
| `Botao` com só `<Icone>` | Moldura.jsx:134; BlocoCliente.jsx:225; MenuDoPedido.jsx:29; BalaoAssistente.jsx:313,404; Cartoes.jsx:16; Sininho.jsx:64,288 |
| `<button>` cru + Icone | Modal.jsx:100 (fecharCanto); Toast.jsx:27; Chip.jsx:61; PainelMovel.jsx:145,147,152; SugestaoDeTexto.jsx:7-8 (caractere ×); BarraBalcao.jsx:132; NotasDoCliente.jsx:25,45; TagsDoCliente.jsx:99; CampoEmLinha.jsx:122,131 |
O "X" tem 4 glifos/tamanhos: `fechar` 20 (Aviso.jsx:91), `x` 18 (Modal.jsx:101, Toast.jsx:28), `x` 20 (Chip.jsx:62, BarraBalcao.jsx:136), caractere "×" (SugestaoDeTexto.jsx:8).
ÚNICA: `BotaoIcone`. FALTA: presets de `tamanhoIcone` por token; slot de indicador (ponto/contagem) para sino, filtro e Assistente; variante sobre fundo invertido (Aviso.module.css:23 força `color: inherit`); o `min-width` inline do BotaoIcone (BotaoIcone.jsx:30) é anulado por `.dispensar` (assistente.module.css:102), que o deixa 26x40.

2.3 BOTÃO CRU `<button>` (≈55). Padrões reais:
- cartão-botão: CartaoConversa.jsx:100; PainelFicha.jsx:56 (`AcessoDaFicha`); CartaoDoCliente.jsx:24
- link-botão: PainelConversas.jsx:52 e BarraBalcao.jsx:169 (`.linkVazio`); BarraProximoPasso.jsx:250 (`.linkMotivo`); BlocoCliente.jsx:394; AvisosDoCliente.jsx:82
- selo-botão: Balao.jsx:113,129,136
- item de menu: MenuDoPedido.jsx:44-71; BlocoCliente.jsx:238-244; BarraBalcao.jsx:112
- stepper: BlocoPedido.jsx:82,86; ComandaDoCliente.jsx:199,201 (sem CSS de botão: ficam com o cinza do navegador)
- chips de tag: TagsDoCliente.jsx:96,99,126,140,152,165
ÚNICA: `Botao`. FALTA: `variante="link"` e `variante="item"` (alinhado à esquerda, largura total, sem fundo) e `[aria-pressed='true']` no CSS do Botao (hoje só o BotaoIcone estiliza, BotaoIcone.module.css:7).

2.4 ESTADO: selo / pílula / chip
- `Pilula` (ponto): 17 usos; `fina` sem regra (BarraModo.jsx:60 sim, Thread.jsx:33 não, BlocoOcorrencia.jsx:64 não).
- `Pilula`+`icone` (fundo pastel): só Moldura.jsx:197. PainelAtendimento.jsx:151 põe `<Icone>` como filho em vez de usar `icone=`.
- `Selo`: 0 usos (componentes/Selo.jsx:10; só a prova ferramentas/prova-2146-componentes-gramatica.mjs:50-56 o cita).
- CSS próprio, mesmo conceito: `.estado_*`/`.itemContexto` (CartaoConversa.jsx:155,160; caixa.module.css:99-115), `.selo`/`.seloBotao` (Balao.jsx:115-136; atendimento.module.css:208-215), `.seloCaptado` (BlocoCliente.jsx:64), `.seloPontos` (:446), `.seloInformado` (ContatoInformado.jsx:47), `.capturaAtivo/.capturaParado` (BlocoCapturaAutomatica.jsx:44), `.tagRestricao` (CartaoDoCliente.jsx:42), `.chipRestricao` (TagsDoCliente.jsx:94), `.lojaAberta/.lojaFechada` (Moldura.jsx:321-330; moldura.module.css:244-247).
ÚNICA: `Pilula`, com `Selo` como forma com ícone. FALTA: tom `info` no Selo (`TONS_DO_SELO`, Selo.jsx:6-8, só tem 5), tamanho "inline" 13px para linha de contexto, modo botão (para "automática"/"Reenviar"), regra escrita para `fina`.

2.5 INDICADOR DE QUANTIDADE (8 desenhos)
| Onde | Altura | Arquivo:linha |
|---|---|---|
| `.conta` (Automáticas) | 18 (26 morto) | moldura.module.css:30,224; Moldura.jsx:244 |
| `.contaTrilho` | 18 (20 morto) | moldura.module.css:164,224; Moldura.jsx:347 |
| `.seloTrilho` (Entregas) | 18 (20 morto) | moldura.module.css:171,225 |
| `BarraDeAbas .contador` | 18 (20 morto) | BarraDeAbas.module.css:13,19; BarraDeAbas.jsx:50 |
| `Sininho .badge` | 18 (26 morto) | lembretes.module.css:17,136; Sininho.jsx:281 |
| `.naoLidas` (cartão) | 20, fundo `--acao` | caixa.module.css:79-85; CartaoConversa.jsx:146 |
| `.numeroChip` | só número | caixa.module.css:214; BarraBalcao.jsx:48,143,147 |
| mobile `.controles .conta` | 22 | moldura.module.css:193 |
Pontos de estado: 7px (caixa.module.css:223), 8px (:120, Pilula `--ponto`), 10px (:176, lembretes.module.css:71, assistente.module.css:36), cantos `50%` em caixa:120 e `--r-1` em caixa:177,224 (redondo e quadrado na mesma tela).
ÚNICA: `Pilula` com prop `contagem` (tom neutro/erro/marca). FALTA: essa prop.

2.6 CHIP
`Chip` (3 usos nas telas: Cartoes.jsx:31, EscolhaDeMeio.jsx:38, FolhaResumo.jsx:53). Concorrentes: `.chipFiltro/.chipLigado/.chipCanal` (BarraBalcao.jsx:39-51,142,145; caixa.module.css:202-221, 13px), `.chip*` das tags (TagsDoCliente.jsx:94-152; cliente.module.css:129-158), `button.pilula` (ModalProgramarLembrete.jsx:137; lembretes.module.css:121), `.tagsCliente li` (CartaoDoCliente.jsx:41; ficha.module.css:539). Tag tem 4 desenhos (Chip papel="tag" existe e só o Assistente usa).
ÚNICA: `Chip`. FALTA: `papel="tag"` editável (input inline), `papel="segmento"` com contagem (Todas/Precisa), fonte única (Chip.module.css:10 usa 15px, `.chipFiltro` 13px).

2.7 AVATAR / TILE DE ÍCONE
`Avatar` 32/40/48/64 (Avatar.module.css:3-7): PainelAtendimento.jsx:133, CartaoConversa.jsx:107, BlocoCliente.jsx:210, CartaoDoCliente.jsx:26, MenuDaConta.jsx:72. `AvatarAssistente` em 28 (BalaoAssistente.jsx:258,274; PainelAgente.jsx:71), 32 (AreaColaborativaIa.jsx:97), 36 (BalaoAssistente.jsx:258; BlocoCliente.jsx:276): nenhum bate com a escala do Avatar. Tiles de ícone em 30 (cliente.module.css:213,274 com raio 8px), 32 (bolhaAvaliacao.module.css:10), 36 (ficha.module.css:68), 40 (bolhaAnexo.module.css:11), 48 (Vazio.module.css:8; composerAnexos.module.css:18,21).
ÚNICA: `Avatar`. FALTA: `tipo="assistente"` em tamanho da escala; prop `icone` (tile) com os mesmos 3 tamanhos.

2.8 CARTÃO / BLOCO
`Bloco` (componentes/Bloco.jsx:13): 10 consumidores, todos dentro da ficha (BlocoCliente, BlocoCobranca, BlocoDevolucoes, BlocoEnderecoCapturado, BlocoEntrega, BlocoFreteApi, BlocoOcorrencia, BlocoOcorrenciasApi, SeletorJanela, SeletorJanelaApi). A coluna Ficha em si NÃO usa Bloco: `.resumoAtual` (ficha.module.css:184), `.cartaoCliente` (:525), `.capturaAuto` (:466), `.avisoBloqueio` (:388), `.avisoDiferenca` (:404), `.baixaDivergente` (:292), `.confirmarTroca` (:318), `.cupomAplicado` (:512), `.area` (AreaColaborativaIa.module.css:1), `Cartao` do Assistente (Cartoes.jsx:11), `.itemNaoEntregue` (caixa.module.css:272), `.contatoInformado` (cliente.module.css:282).
Superfícies neutras em uso: `--papel` 44x, `--elevado` 42x, `--preenchimento` 36x, `--preenchimento-forte` 32x, `--fundo` 7x. `--elevado` (#F7F6F2, tokens.css:38) e `--fundo` (#F5F5F1, :36) diferem 2/255 por canal: o "cartão" quase não se lê.
ÚNICA: `Bloco`. FALTA: `tom` (neutro|alerta|erro|sucesso|info, com trilho `--trilho`), `icone` no título, título como nó, `como` (section/article), variante de fundo única.

2.9 SEÇÃO RECOLHÍVEL (6 desenhos, 3 comportamentos de seta)
| Desenho | Onde | Seta |
|---|---|---|
| `Bloco chave=` | Bloco.jsx:30-41 | chevron-right 16, gira 90° |
| `GrupoRecolhido` | PainelConversas.jsx:21-29 | chevron-right 20, gira 90° (caixa.module.css:253) |
| `<details>` ×17 | ComandaDoCliente.jsx:47,165,181,205,223,232,240,347; PainelFicha.jsx:280; AreaColaborativaIa.jsx:100; ModalEncerrar.jsx:232; ModalEnviarCardapio.jsx:85; ModalProgramarMensagem.jsx:184; Thread.jsx:60; + 3 menus | sem seta (marcador do navegador), exceto PainelFicha.jsx:280 chevron-down 16 gira 180° |
| `.abrirNotas` | NotasDoCliente.jsx:82-83 | chevron-down 16, gira 180° (NotasDoCliente.module.css:5) |
| `.resumoAndamento` | BarraProximoPasso.jsx:496-499 | troca chevron-up/down + texto "Ver etapas/Recolher" |
| `Botao .botaoVistos` | Sininho.jsx (Vistos) | sem seta |
ÚNICA: `Bloco`. FALTA: recolhível SEM `chave` (estado local; hoje exige `useRecolhido` persistente), título/resumo como nó (os `<summary>` de ComandaDoCliente carregam valor e nome), seta única (right → 90°).

2.10 MODAL / CONFIRMAÇÃO
`Modal` 21 usos. Larguras: 560 padrão, 680 (PainelFicha.jsx:350), 740 (NaoEntregues.jsx:154), 760 (PainelFicha.jsx:370; ComandaDoCliente.jsx:313), 820 (ModalCliente.jsx:55), 880 (ModalHistorico.jsx:262), 900 (ConsultaFreteCliente.jsx:34). Confirmação como Modal: BarraModo.jsx:90, ModalBloqueio.jsx:30,65,91, SeletorJanela.jsx:77. Confirmação em linha: BarraProximoPasso (`confirmarCorrecao`, ficha.module.css:132), EstornosOnline.jsx:75. Camadas "tipo modal" que não são Modal: ModalNota (`<dialog open>`), Gaveta, PainelMovel, painel do Assistente.
ÚNICA: `Modal`. FALTA: preset de confirmação (`tom`, `rotuloConfirmar`, `rotuloCancelar`), largura por token (sm/md/lg), X do canto como `BotaoIcone`, ordem fixa do rodapé.

2.11 AVISO / ERRO / TOAST
`Aviso` (snackbar; 1 uso nas telas: Composer.jsx:70) e `Toast` (PilhaDeAvisos) são dois mecanismos (Aviso.jsx:11 já diz que a unificação "fica para a Onda 2"). Além deles: `FaixaApi` (FaixaApi.jsx:~65), `AvisoDaAcaoApi` (:27), `.alerta` da Ficha (PainelFicha.jsx:232), 53 `role="alert"`. Classes diferentes só para "mensagem de erro": `.erroAssumir`, `.restricao`, `composerAnexos .erro`, `.programarErro`, `.erroNaoEntregue`, `AreaColaborativaIa .erro`, `.avisoErro`, `.motivoDesabilitado`, `.erroEmLinha`, `.avisoDiferenca`, `.corpoBloco`, `.recusaEndereco`, `.diferencaPaga`, `.erroPdf`, `ModalCliente .erro`, `ModalNota .erro`, `.acaoErro`, `.respostaErro`, `.erroAoEncerrar`, `lembretes .erro`, `.falhou` + 12 `<p role="alert">` sem classe (PassarConversa.jsx:74,91; ComandaDoCliente.jsx:111,112,180,292,315,340; ConsultaFreteCliente.jsx:51; ModalEnviarCardapio.jsx:73; ModalEnviarModelo.jsx:91). Callout com trilho lateral reimplementado 11x (ficha.module.css:138,215,230,345,382,391,407; lembretes.module.css:112; encerramento.module.css:22,47; Bloco.module.css:13).
Fechar aviso: 4 implementações (Aviso.jsx:91 BotaoIcone; Toast.jsx:27 cru; FaixaApi.jsx `.sair` cru; AvisoDaAcaoApi cru).
ÚNICA: `Aviso`. FALTA: `variante="embutido"` (callout) com `tom` (erro/alerta/sucesso/info), `role` automático (alert só em erro), `acao` e `carregando`; absorver o Toast; fechar via BotaoIcone.

2.12 VAZIO e CARREGANDO
Vazio: `Vazio` (10 usos, padding `--e-6`, ícone 48) contra `VazioEmLinha` (PainelConversas.jsx:48; caixa.module.css:236), `.vazio` (SeletorRespostas.jsx:49; BalaoAssistente.jsx:320; Sininho.jsx:302), `css.vazio` usado para "Frete R$" (BlocoPedido.jsx:220) e ≈14 `<p>Nenhum…</p>` crus (PassarConversa.jsx:79; ModalEnviarModelo.jsx:96; ModalProgramarMensagem.jsx:191; AreaColaborativaIa.jsx:53; BlocoDevolucoes.jsx:83; BlocoOcorrenciasApi.jsx:90; ModalHistorico.jsx:171; FolhaResumo.jsx:134; PainelFicha.jsx:387…). ÚNICA: `Vazio`. FALTA: `variante="linha"` (sem ícone, à esquerda, ação inline).
Carregando: não há componente. Verbos: Carregando (Sininho.jsx:299; ModalEnviarModelo.jsx:95), Consultando (PassarConversa.jsx:72; ConsultaFreteCliente.jsx:39), Conferindo (NaoEntregues.jsx:149), Atualizando (NaoEntregues.jsx:161), Organizando (AreaColaborativaIa.jsx:106). 2 spinners CSS (atendimento.module.css:96-101; composerAnexos.module.css:166-171). ≥15 botões trocam o texto ("Salvando…", "Enviando…", "Reenviando…"). FALTA: `Aviso embutido carregando` + prop `ocupado` no Botao.

2.13 CAMPO
`Campo*` 46 usos. Crus: busca (BarraBalcao.jsx:127 com `.campoBusca` caixa.module.css:157; SeletorRespostas.jsx:30 com `.busca`), checkbox ×6 (ModalEnviarModelo.jsx:118; ModalProgramarMensagem.jsx:153; BlocoDevolucoes.jsx:95; EstornosOnline.jsx:91; ModalEncerrar.jsx:192,202), range ×2 (Moldura.jsx:295,298), number (ComandaDoCliente.jsx:250), `<select>` (BarraBalcao.jsx:176; ComandaDoCliente.jsx:216). FALTA em Campo.jsx: `CampoBusca`, `CampoCaixa`, `CampoIntervalo`; usar `CampoSelecao` nos dois selects.

2.14 ABAS
`BarraDeAbas` (Moldura.jsx:648; ModalHistorico.jsx:235) tem 3 camadas empilhadas (`.aba` em :6/:23/:37; `.contador` :13/:19; `.ativa` :12/:28). Todas/Precisa de você (BarraBalcao.jsx:140-149) é segmento disfarçado de chip. FALTA: `variante="segmentos"`; consolidar o CSS.

2.15 DICA (tooltip): `Tooltip` 8 usos nas telas (Composer/ComposerAnexos) contra 47 `title=` nativos (Moldura.jsx:140,194,223,234…; atraso e aparência diferentes). FALTA: prop `dica` no Botao.

====================================================================
3. VOCABULÁRIO (o que o usuário vê)
====================================================================
| Conceito | Variantes (arquivo:linha) | Recomendado |
|---|---|---|
| Quem responde sozinho | "Assistente" (BarraModo.jsx:38,78,91,97; resumoDoModo.js:13; BalaoAssistente.jsx:252,274); "automático/automática" (Balao.jsx:119 selo e :116 title; Thread.jsx:48 "automática digitando"; BlocoCapturaAutomatica.jsx:35-37,43 "o automático"/"Atendimento automático"; PainelAgente.jsx:120); "agente" (Balao.jsx:116; ModalHistorico.jsx:189; BlocoOcorrenciasApi.jsx:13); "IA" (Composer.jsx:380 "Sugestões de IA"; AreaColaborativaIa.jsx:98 "Organizar com IA"); "robô": não aparece | "Assistente" em todo texto de ator. "Sugestões" para a função. Selo da bolha: "Assistente". |
| Regras (topo/trilho) | "Automáticas" (Moldura.jsx:208,241,344), e "Automáticas" também é a aba de mensagens enviadas (ModalHistorico.jsx:243) | Regras: "Automações". Aba: "Enviadas pelo Assistente". |
| Encerrar a conversa | "Concluir conversa" (PainelAtendimento.jsx:173; BarraModo.jsx:81; BarraProximoPasso.jsx:182,358; ModalEncerrar.jsx:158,175); "Encerrar atendimento" (MenuDoPedido.jsx:73; RelogioPix.jsx:209); estado "Encerrada" (resumoDoModo.js:16; PainelConversas.jsx:183), "Conversa encerrada" (Composer.jsx:204), "Conversa concluída" (PainelFicha.jsx:246; automatico.js:103), "Atendimento N encerrado" (Thread.jsx:21), "Encerrado por" (FolhaResumo.jsx:262); na MESMA modal: título "Resumo do atendimento N" (ModalEncerrar.jsx:158) e "Ver resumo da conversa" (:233) | Ação "Concluir conversa"; estado "Concluída"; grupo "Concluídas"; "Reabrir conversa". "Atendimento" só para o ciclo numerado do histórico. |
| Comanda / Pedido | "Comanda N" h3 (PainelFicha.jsx:246); "Revisar comanda" (:158,263); "Consultar pedido vinculado" (:263); "Comanda · nome" (:311); "Nova comanda" (:259); "Cancelar pedido" (MenuDoPedido.jsx:46); "Confirmar pedido com o cliente" (ComandaDoCliente.jsx:101) | DECISÃO DO DONO. Sugestão: "Comanda" até a cobrança; "Pedido" só no que já foi criado (cancelar, entregue). Trocar "Consultar pedido vinculado" por "Ver comanda". |
| Ficha / Resumo / Cadastro | "Ficha" = coluna (PainelFicha.jsx:220; Moldura.jsx:583,604,654), = modal de cadastro (ModalCliente.jsx:55,61), = PDF (BotaoBaixarFicha.jsx:23); "Resumo" = a coluna (LimiteDeErro `parte="Resumo"` Moldura.jsx:584,605,624; aria PainelFicha.jsx:245), = fechamento (ModalEncerrar), = IA (BlocoCliente.jsx:275 "Resumo do assistente"); "Editar cadastro" (PainelFicha.jsx:223), "Cadastrar cliente" (:207), "Histórico do cliente" (:275), "Cliente" (BlocoCliente.jsx:207) | "Ficha" = só a coluna (e o PDF). Modal = "Cadastro do cliente". "Resumo" só no fechamento e no resumo do Assistente. Corrigir `parte="Resumo"` para "Ficha". |
| Lista / fio / módulo | "Conversas" coluna (Moldura.jsx:562; BarraBalcao.jsx:92); aba "Conversas" vs "Conversa" (Moldura.jsx:652-653); "Atendimento" módulo (Moldura.jsx:332-334); "Balcão" em texto visível (ModalEnviarModelo.jsx:82; dominio/agora.js:43; dominio/guiaDoConsole.js:9,11) | "Conversas" (lista) e "Conversa" (fio). Tirar "Balcão" do texto visível. |
| Assumir / devolver / passar | "Assumir" (BarraModo.jsx:65); "Devolver ao Assistente" (:79,91,97); "Continuar comigo" (:96); "Passar para…" (PassarConversa.jsx:62); "Passou para você" (resumoDoModo.js:15; CartaoConversa.jsx:25; BlocoCapturaAutomatica.jsx:35); "Precisa de você" (BarraBalcao.jsx:147); "Esperando você" (CartaoConversa.jsx:27); "Devolver" também é dinheiro: "a devolver" (PainelFicha.jsx:252; apresentacaoDaFicha.js:17). "Transferir" só existe em código/API (conversasApi.js:58), não na tela. | Assumir / Devolver ao Assistente / Passar para… (pessoa). "Precisa de você" para o filtro e o estado. Dinheiro nunca "devolver": ver estorno. |
| Estorno / devolução / reembolso | "Marcar estorno" (BarraProximoPasso.jsx:636); "Cancelado com estorno" (:179); "Devoluções" (BlocoDevolucoes.jsx:65); "Confirmar devolução já realizada" (:97); "reembolso" (BlocoOcorrenciasApi.jsx:64) | "Estorno" para a ação; "Devolução" para o registro. Eliminar "reembolso". |
| Nota | "Nota interna" (Composer.jsx:321; Thread.jsx:61; ModalNota.jsx:130); "Nota" (Composer.jsx:325; NotasDoCliente.jsx:84-85); "Fixar nota"/"Salvar"/"Salvar nota"/"Nota salva" (ModalNota.jsx:157; ModalHistorico.jsx:167); "Destacar nota"/"Nota solta"/"Recolher à ficha" (NotasDoCliente.jsx:26,34) | "Nota interna" (objeto) e "Salvar nota" (ação). "Destacar" ok se for o único termo de soltar. |
| Cardápio / link | "Enviar cardápio" (CompartilharComanda.jsx:27; ModalEnviarCardapio.jsx:60); "Link do cliente" (PainelFicha.jsx:280; ComandaDoCliente.jsx:291); "comanda pessoal" (ModalEnviarCardapio.jsx:90); "Abrir cardápio" (AreaColaborativaIa.jsx:38); "Itens" (PainelFicha.jsx:208,210) | Objeto "Cardápio do cliente"; ação "Enviar cardápio"; "Abrir cardápio" só para a atendente montar itens. |
| Fechar / cancelar | "Cancelar" (ModalBloqueio.jsx:36,97; ModalProgramarLembrete.jsx:62; ModalEncerrar.jsx:163); "Voltar" (PassarConversa.jsx:70; BlocoCobranca.jsx:144,200,245); "Fechar" (ModalEnviarModelo.jsx:85; ModalProgramarMensagem.jsx:124; ModalCliente.jsx:58; Canhoto.jsx:90); "Recolher" (ModalNota.jsx:151); "Continuar comigo" (BarraModo.jsx:96); "Entendi" (PainelFicha.jsx:235); "Dispensar" (Cartoes.jsx:16) | Modal sem decisão: "Fechar". Com decisão: "Cancelar" + verbo da ação. "Voltar" só entre etapas. |
| Janela | WhatsApp 24 h: "Janela fechada" (Composer.jsx:272); entrega: seletor de janela (SeletorJanela.jsx; PainelFicha.jsx:192 `focarCampoDaFicha('janela')`); Entregas "ocupação de cada janela" (Moldura.jsx:101) | "Janela de resposta" (24 h) e "Horário de entrega". |
| Respostas | "Respostas" botão (Composer.jsx:317); "Respostas prontas" (Composer.jsx:307; SeletorRespostas.jsx:201); "Respostas rápidas" (SeletorRespostas.jsx:176); "Modelos aprovados" (Composer.jsx:225; Balao.jsx:131) | "Respostas prontas" (texto livre) e "Modelos aprovados" (WhatsApp). Remover "rápidas". |
| Avisos | "Avisos" sino (SinoDaSessao.jsx:34), "Lembretes" relógio (Sininho.jsx:99), snackbar `Aviso`, `.alerta` "Entendi" (PainelFicha.jsx:232), "Não entregues" | "Avisos" = notificações do sistema; "Lembretes" = tarefas; snackbar sem nome. |
| Bloqueado | Pilula "Bloqueado" (PainelAtendimento.jsx:151) + Pilula "Bloqueado" na BarraModo (resumoDoModo.js:17) + linha "Envios bloqueados" (:24) + "Cadastro bloqueado" (BlocoCliente.jsx:260ss) + aviso do Composer (Composer.jsx:213-218): 4 faixas do mesmo estado ao mesmo tempo | Um aviso só: "Cadastro bloqueado" (BarraModo); tirar a Pilula do cabeçalho. |
| Responsável | "Responsável"/"Comigo"/"Sem responsável" (BarraBalcao.jsx:175-179); valor `sem-dono`; "outra atendente" (PassarConversa.jsx:62); "a dona ou um gerente" (BlocoOcorrenciasApi.jsx:66); nome fixo 'Thatiane' (Moldura.jsx:451) e classe `.perguntaDaThatiane` (assistente.module.css:119) | "Responsável" (ou "atendente", escolher um). |
| Carregando | Carregando / Consultando / Conferindo / Atualizando / Organizando (ver 2.12) | "Carregando…" para ler; "Salvando…/Enviando…" para gravar. |

====================================================================
4. HARMONIA (valores nas folhas CSS das telas)
====================================================================
Escala que deveria valer: controle = `--toque` (40 / 48 no toque); região = 64; ícone 16 / 20 / 24 (+32 destaque); fonte 13 / 14 / 15 / 17 / 20 / 24 por PAPEL; espaço 4-8-12-16-24-32; raio e cor SÓ por token; elevação por camada.

4.1 FONTE (font-size)
| Valor | Contagem | Observação |
|---|---|---|
| `--t-apoio` 14 | 191 | controles, corpo secundário |
| `--t-corpo` 15 | 36 | Chip (Chip.module.css:10), Popover item (:29), Campo (:12) |
| `--t-micro` 13 | 35 | + `--t-minimo` 13 (29): MESMO valor, 2 nomes (tokens.css:106-107; 192 repete micro) |
| `--t-forte` 17 | 19 | |
| `--t-titulo` 20 | 12 | |
| `--t-valor` 24 | 8 | |
Desvios: `.75rem` 12px (Trilho.module.css:16,25), `11px` (moldura.module.css:167, morto por :224), `10px` (Avatar.module.css:26), `20px` (PainelMovel.module.css:8), `24px` (atendimento.module.css:523, post-it), `1rem` (caixa.module.css:267 faixa Não entregues; Campo.module.css:23 mobile), `1.5rem` (NotasDoCliente.module.css:19; ModalCliente.module.css:38), `1.125rem` (ModalNota.module.css:36), `0` como truque (moldura.module.css:192; atendimento.module.css:419,422).
Mesmo papel, tamanhos diferentes (rótulo de controle): Botao 14 (Botao.module.css:7), Chip 15, `.chipFiltro` 13 (caixa.module.css:206), `.gerenciar` 13 (seletorRespostas.module.css:272), `.itemMenu` 14, Popover item 15+14, aba 13/14 (BarraDeAbas.module.css:25,39), Trilho 12.
Título de região: Conversas h2 20 (caixa.module.css:130), Ficha h2 20 (ficha.module.css:165), nome da conversa h2 17 (atendimento.module.css:9), Lembretes 20 (lembretes.module.css:46), Assistente 14 (assistente.module.css:80), Modal 20/700 mas ModalCliente 20/600 (ModalCliente.module.css:2).

4.2 ALTURA
| Valor | Onde |
|---|---|
| `--toque` | 86 min-height + 25 height (Botao, Chip, Campo, BotaoIcone…) |
| 64px | moldura.module.css:18 (topo); atendimento.module.css:389 (cabeçalho da conversa); caixa.module.css:129; ficha.module.css:164 (+ :230 e ModalNota:35 que não são cabeçalho) |
| 44px literal | atendimento.module.css:461,462 (`--toque` é 40/48; comentários de BotaoIcone.jsx:15 e moldura.module.css:251 dizem 44) |
| 48px | ficha.module.css:309 (`.chipDestaque` + fonte 17); ModalNota.module.css:39 |
| 52px | moldura.module.css:85 (morto por :230); InterruptorTema.module.css:15 |
| 54px | BarraDeAbas.module.css:25; token `--altura-item-trilho` (Trilho) |
| 56px | atendimento.module.css:172 (textarea, depois sobrescrito em :348) |
| botão flutuante | assistente.module.css:8 (56x56), :156 (52x52) |
| contadores | 18 / 20 / 22 / 26 (ver 2.5) |

4.3 CLASSES QUE ENCOLHEM OU AUMENTAM `Botao`
- Encolhem: `.controles > button { font-size:0; padding:0 10px }` (moldura.module.css:191-197; 8px em :204) apaga o texto de TODO botão do topo no celular; `.dispensar` (assistente.module.css:102) 26x40 (BotaoIcone é 40x40); `.acoes > button:not(.enviar)` (atendimento.module.css:342) faz "Respostas/Nota" virarem quadrados 40 e `.botaoRespostas`/`.botaoNota` (:361-370) reabrem com paddings diferentes (`0 12px 0 10px` gap 5 contra `0 8px` gap 4); `.botoesPrimarios > button { width:auto; padding:0 16px }` (ficha.module.css:457-458); `.itemTrilho` (Trilho.module.css:21-26) transforma Botao em tile 68 de fonte 12 e `letter-spacing -.035em`; `.botaoSimular` com `!important` ×4 (moldura.module.css:126-129,137).
- Aumentam: `.chipDestaque` 48 (ficha.module.css:309); `.embutida .acoes button` 48 (ModalNota.module.css:39); `.gatilho` do Assistente 56/52 (assistente.module.css:8,156); `.itemTrilho` 54.
- Não casam mais (ver seção 5): `.direita > button.encerrar/.passar` (atendimento.module.css:419-423), `.acoes > button.acaoComRotulo`/`.acoes > label` (:393-399, 491-504), `.acoes > button.chaveLigada` (:285).

4.4 RAIO
| Valor | Contagem | Observação |
|---|---|---|
| `--r-1` | 89 (+3 compostos) | = 0px |
| `--r-pilula` | 32 (+1 `!important`) | = 0px |
| `--r-2` | 31 | = 0px |
| `--r-ponta` | 4 | = 0px |
| `--r-3` | 1+1 | = 0px (Modal.module.css:4,48) |
| literal `50%` | 5 | atendimento.module.css:97,442; composerAnexos.module.css:57; caixa.module.css:120; ficha.module.css:493 |
| literal `999px` | 1 | composerAnexos.module.css:28 (`.previaRemover`, único botão redondo do app) |
| literal `8px` | 2 | cliente.module.css:213,274 |
| literal `2px` | 3 | bolhaAnexo.module.css:33; composerAnexos.module.css:39; ficha.module.css:51 |
Cinco tokens com o mesmo valor 0px (tokens.css:135-139): a hierarquia "controle/cartão/painel" não existe. Ou fica tudo reto (trocar os 12 literais por token) ou os tokens passam a valer algo; hoje cada literal é uma exceção visível.

4.5 ESPAÇO (fora da grade de 4 px)
| Propriedade | Literais fora da grade | Principais |
|---|---|---|
| gap | 36 | 2px ×15 (moldura.module.css:202,203; Toast.module.css:26; BarraDeAbas.module.css:24…), 6px ×9 (moldura:192; Pilula:33; Trilho:9…), 3px ×5, 5px ×4 (atendimento:362,440; caixa:220; ficha:487) |
| padding | 39 | 10px ×9 (moldura:192; atendimento:197,362,419,422; caixa:220…), 2px ×10, 6px ×5, 5px ×5 (moldura:224,225; BarraDeAbas:19; lembretes:136), 7px ×3 (moldura:31; caixa:110; cliente:241), 3px ×3, 9px ×1 (cliente:256) |
| margin | 10 | 2px ×4, 6px ×2, 5px, 3px, ±1px |
`--e-16`=`--e-4` (13 usos) e `--e-24`=`--e-5` (2) são nomes duplicados (tokens.css:130-131); `--e-0` vale 4px (= `--e-1`, :132). Não existe token para 2px.

4.6 COR
Só 3 literais, todos em sombra: ficha.module.css:76 (`rgba(43,106,69,.55)`), cliente.module.css:214, comanda.module.css:9. Fora disso tudo é token. Token inexistente: `var(--info-fundo)` em cliente.module.css:284 (sem fallback; o fundo some). `--linha-media` e `--linha-forte` têm o mesmo valor (tokens.css:46-47; 10 e 12 usos).

4.7 ELEVAÇÃO E BORDA
Menu/popover: `--sombra-media` (Popover.module.css:9, MenuDaConta:14) contra `--sombra` pequena (atendimento.module.css:38,255; lembretes.module.css:38; cliente.module.css:85). Painéis: Modal `--sombra-media` (Modal.module.css:8), Gaveta e Assistente `--sombra-alta` (moldura.module.css:103; assistente.module.css:47) e o Assistente ainda tem borda 1px (assistente.module.css:46) e 4 divisórias 1px (:76,92,115,148), contra a regra do Bloco ("separado por espaço, nunca por linha", Bloco.module.css:1). Borda 1px sólida por arquivo: encerramento 9, ficha 8, assistente 8, caixa 5.

4.8 ÍCONES
| Tamanho | Usos | Onde |
|---|---|---|
| 12 | 3 | CartaoConversa.jsx:111; BlocoCapturaAutomatica.jsx:54; BlocoCliente.jsx:65 |
| 13 | 1 | BlocoCliente.jsx:447 |
| 14 | 6 | Balao.jsx:126; CartaoConversa.jsx:138,156,161; AcoesPropostas.jsx:45; AssuntoEmail.jsx:11 |
| 15 | 3 | BlocoCliente.jsx:332,343,358 |
| 16 | 12 | BarraBalcao.jsx:47; BarraProximoPasso.jsx:499,541; Bloco.jsx:39; CartaoDoCliente.jsx:43… |
| 18 | 14 | NotasDoCliente.jsx:23,28; PainelMovel.jsx:141-147; Modal.jsx:101; Toast.jsx:28; PainelFicha.jsx:59… |
| 20 | 33 | maioria; default de Botao (Botao.jsx:19) e BotaoIcone |
| 22 | 6 | Moldura.jsx:289,328,341,345,350,365 (trilho) |
| 24 | 10 | MenuDoPedido.jsx:38; BlocoCliente.jsx:233; Sininho.jsx:54,69,79,279,292 |
| 28/32/36/40 | 1/3/1/1 | BalaoAssistente (28,36); RelogioPix.jsx:127,147; BarraProximoPasso.jsx:178,528 |
| sem tamanho | 64 | `Icone.jsx:172` default `1.15em` |
Mais overrides por CSS: 20px (atendimento.module.css:311,399,420,423; moldura.module.css:260; ficha.module.css:87), 16px (caixa.module.css:221), 18px (ModalNota.module.css:40). Mesmo ícone, 3 tamanhos: ellipsis em 17 / 20 / 24; chevron-right em 16 (Bloco.jsx:39) / 18 (PainelFicha.jsx:59) / 20 (PainelConversas.jsx:27).
Aliases com dois nomes por glifo em `Icone.jsx`: `ellipsis`=`ellipsis-vertical` (:44), `fechar`=`x` (:83), `lupa/search/busca` (:79), `panela/cooking-pot` (:124), `mais/plus` (:143), `pega/grip-vertical` (:167).

====================================================================
5. O QUE NÃO DEVERIA EXISTIR
====================================================================
COMPONENTE MORTO
- `componentes/Selo.jsx` (0 usos; só ferramentas/prova-2146-componentes-gramatica.mjs:50-56, e o comentário dela diz "primeira adoção no Composer", que não houve).
- Ramo de título de `Coluna` (Moldura.jsx:41,46-57; moldura.module.css:83-89,230-231): as 3 chamadas passam `semTitulo`.

DUPLICADO
- `CompartilharComanda` montado duas vezes: PainelAtendimento.jsx:162 (menu do cabeçalho) e Moldura.jsx:468 → PainelFicha → ComandaDoCliente.jsx:291 ("Link do cliente").
- Tags com 4 desenhos (2.6). Menu/overflow com 7 (2.1). Fechar "X" com 8 glifos/implementações (2.2).
- Mesmo número duas vezes: "Conversas N" (BarraBalcao.jsx:92) e chip "Todas N" (:143), apesar do comentário Moldura.jsx:560-561 ("duas vezes é ruído").
- Três coisas chamadas `Cabecalho`: Moldura.jsx:210 (topo), ficha-cliente/Cabecalho.jsx (papel da comanda), `.cabecalho` em 5 folhas.
- `ModalNota` não é Modal (`<dialog open>`, ModalNota.jsx:128); `ModalHistorico embutido` não é modal (ModalHistorico.jsx:261).
- Tokens duplicados: `--t-minimo`=`--t-micro`, `--e-16`=`--e-4`, `--e-24`=`--e-5`, `--e-0`=`--e-1`, `--linha-media`=`--linha-forte`, `--marca`=`--acao`, 5 raios = 0px.
- Declarações sobrescritas no MESMO arquivo (a primeira morre): moldura.module.css `.conta` 30/224, `.contaTrilho` 164/170/224/226, `.seloTrilho` 164/171/225/226, `.tituloColuna` 83/230, `.espacoTrilho` 184/233; BarraDeAbas.module.css `.aba` 6/23/37, `.ativa` 12/28, `.contador` 13/19; lembretes.module.css `.badge` 17/136; atendimento.module.css `.cabecalho` 1/389, `.caixa` 160/335, `.acoes` 180/341, `.entrada` 102/380, `.saida` 107/381, `.sistema` 116/383, `.saida.doAutomatico` 295/382, `.identidade` 7/26; Avatar.module.css `.selo` 22/30.

CSS ÓRFÃO (classe definida, nunca aplicada)
- ficha.module.css: `.remover`:10, `.contagemEtapa`:101, `.pedidoAntigo`:148 (+149-156), `.tags`:158, `.enderecoCapturado`:161, `.fase`:189, `.acoesCliente`:400 (existe a viva em NotasDoCliente.module.css:156), `.tag`:424, 10 `.canhoto*` (:431-447; o próprio comentário :449-453 diz que ninguém mais usa), `.identidadeClienteAbrir`:526, `.contatoClienteInline`:530, `.notasCliente`:531, `.notaCliente`:532, `.resumoIa`:536, `.resumoIaCarregando`:537, `.postitResumo`:543, `.rotuloPostit`:549.
- comanda.module.css `.estadoCompartilhado`:32. atendimento.module.css `.acaoToque`:262. caixa.module.css `.linhaB`:139, `.chipAtencao`:219, `.divisorFiltros`:226. moldura.module.css `.rolavel`:73, `.espaco`:109, bloco `@media` vazio :115-116. BarraDeAbas.module.css `.icone`:30-31.

CSS APLICADO SEM EFEITO / REGRAS QUE NÃO CASAM MAIS
- `.modo_ligado/.pausado/.com-voce/.encerrado/.bloqueado` (atendimento.module.css:230-234): só `border-left-color`, e `.modo` tem `border:0` (:227). `.bloqueioGrave border-left-color` (:267) idem (`.bloqueio` `border:0`, :199).
- `.direita > button.encerrar/.passar` (atendimento.module.css:419-423): os botões agora estão dentro do `<details>` (PainelAtendimento.jsx:171-175, PassarConversa.jsx:62).
- `.acoes > button.acaoComRotulo`, `.acoes > label`, `.acoes > button.chaveLigada` (atendimento.module.css:285,393-399,491-504): Fotos, Programar, Anexar e "Sugestões de IA" moram em `.menuFerramentas` (Composer.jsx:339-389), não são filhos de `.acoes`. CONSEQUÊNCIA VISÍVEL: o interruptor "Sugestões de IA" (Composer.jsx:382-388) não mostra o estado ligado; só o Tooltip diz.
- `.linhaB` (caixa.module.css:139-152) nunca aplicada: `.rotuloResponsavel` (BarraBalcao.jsx:110,175) só tem estilo dentro dela, então "Ordenar" e "Responsável" saem como texto solto.
- `.tituloColuna` e `.contagem` (moldura.module.css:83-89): ver componente morto.

USO SEM DEFINIÇÃO
- `css.concluido` (BarraProximoPasso.jsx:514), `css.freteCliente` (CartaoDoCliente.jsx:36,37), `var(--info-fundo)` (cliente.module.css:284; existe `--info-pastel`).

====================================================================
6. REFATORAÇÕES PRIORIZADAS (25)
====================================================================
| id | grav. | de -> para | arquivos | risco |
|---|---|---|---|---|
| R01 | P1 | 7 menus de overflow -> `Popover` + gatilho `BotaoIcone` ellipsis-vertical 20, Tooltip e aria "Mais ações"; Popover ganha item com `icone/disabled/tom/aoEscolher`, separador, `portal` padrão, largura única | Popover.jsx/.css; PainelAtendimento.jsx:152-177; BarraModo.jsx:70-89; Composer.jsx:332-390; Moldura.jsx:134-158; MenuDoPedido.jsx; BlocoCliente.jsx:225-263; atendimento.module.css:27-48,252-257 | Alto: foco, Esc dentro de `<dialog>`, portal, testes de a11y |
| R02 | P1 | Todo botão só-ícone -> `BotaoIcone` (X de Modal/Toast/Chip/PainelMovel/SugestaoDeTexto, Assistente, Sininho, Cartoes, busca) com presets de ícone 16/20/24 | Modal.jsx:100; Toast.jsx:27; Chip.jsx:61; PainelMovel.jsx:141-152; SugestaoDeTexto.jsx; BalaoAssistente.jsx:313,404; Cartoes.jsx:16; Sininho.jsx:64,288; BarraBalcao.jsx:132; BotaoIcone.jsx | Médio: muda tamanho de alvo e visual em 15 pontos |
| R03 | P1 | Escala de ícone 16/20/24 (+32): `Icone` default 20 em vez de 1.15em; trocar 12/13/14/15/18/22/28/36 | Icone.jsx:172; 64 `<Icone>` sem tamanho; lista de 4.8; Trilho 22->24 (Moldura.jsx) | Médio: alinhamento de linha e quebra de layout |
| R04 | P1 | Contador único: `Pilula contagem` (18px, tom neutro/erro/marca) no lugar de `.conta`, `.contaTrilho`, `.seloTrilho`, `.contador`, `.badge`, `.naoLidas`, `.numeroChip`; pontos 8px com raio por token | Pilula.jsx/.css; moldura.module.css:30,164-174,224-226; BarraDeAbas.module.css:13,19; lembretes.module.css:17,136; caixa.module.css:79,119,175,214,222; ResumoEntregas.jsx:30 | Baixo |
| R05 | P1 | Estado/selo único: adotar `Selo`/`Pilula+icone` (tom `info` incluso) no lugar de `.estado_*`, `.itemContexto`, `.selo*`, `.seloCaptado`, `.seloPontos`, `.seloInformado`, `.capturaAtivo/Parado`, `.tagRestricao`, `.lojaAberta/Fechada`; regra para `fina` | Selo.jsx:6; Pilula.jsx; CartaoConversa.jsx:155-165; Balao.jsx:113-136; BlocoCliente.jsx:64,446; ContatoInformado.jsx:47; BlocoCapturaAutomatica.jsx:44; Moldura.jsx:189-203 | Médio: cartão da lista é memoizado (#1577), cuidar de perf |
| R06 | P1 | `Aviso variante="embutido"` (tom, role, ação, carregando) no lugar de ≥20 classes de erro e 11 callouts com trilho; Toast absorvido; fechar via BotaoIcone | Aviso.jsx/.css; Toast.jsx; lista 2.11; ficha.module.css:138,215,230,345,382,391,407; encerramento.module.css:22,47; lembretes.module.css:110 | Médio: `role="alert"` vs `status` muda o que o leitor de tela anuncia |
| R07 | P1 | Vocabulário de encerramento: "Concluir conversa"/"Concluída"/"Concluídas"/"Reabrir"; sair "Encerrar atendimento" e "Encerrada" | MenuDoPedido.jsx:73; RelogioPix.jsx:209; resumoDoModo.js:16; automatico.js:103; PainelConversas.jsx:183; Composer.jsx:204; ModalEncerrar.jsx:158,233; FolhaResumo.jsx:262; Thread.jsx:21 | Baixo no código, médio nas provas que procuram texto exato |
| R08 | P1 | Vocabulário do ator: "Assistente" (selo, title, "digitando", aba e origem); "Automações" para as regras; "Sugestões" para IA | Balao.jsx:116,119; Thread.jsx:48; BlocoCapturaAutomatica.jsx:35-43; ModalHistorico.jsx:189,243; BlocoOcorrenciasApi.jsx:13; Composer.jsx:380-387; AreaColaborativaIa.jsx:98; Moldura.jsx:208,241,344; PainelAgente.jsx:120 | Baixo; ferramentas/provas citam "Automáticas" |
| R09 | P1 | Escala de fonte por papel: controle 14, corpo 15, secundário 13 (um só token), título de região 20; sem 12/16/18/24 soltos; unificar `--t-minimo`/`--t-micro` | tokens.css:106-107,192; Chip.module.css:10; caixa.module.css:206,267; Popover.module.css:29; assistente.module.css:80; Trilho.module.css:16,25; NotasDoCliente.module.css:19; ModalCliente.module.css:38; ModalNota.module.css:36 | Médio: 64 pontos de uso do 13 |
| R10 | P2 | Cabeçalho de região único (64px, título 20/700): token `--altura-regiao`; nome da conversa e Assistente alinhados; Modal 700 em todas | moldura.module.css:18; atendimento.module.css:9,389; caixa.module.css:129; ficha.module.css:164; assistente.module.css:73-81; ModalCliente.module.css:2 | Baixo |
| R11 | P2 | Disclosure único: `Bloco` recolhível sem `chave`, título como nó, seta right->90°; absorver `<details>` ×14, `GrupoRecolhido`, `.abrirNotas`, `.resumoAndamento`, `.botaoVistos` | Bloco.jsx:13-47; ComandaDoCliente.jsx (8 `<details>`); PainelFicha.jsx:280; PainelConversas.jsx:15-45; NotasDoCliente.jsx:82; BarraProximoPasso.jsx:496; Sininho.jsx | Médio: teste de abrir/fechar e foco |
| R12 | P2 | Chip único: `Chip papel="segmento"` com contagem (Todas/Precisa) e `papel="tag"` editável; apagar `.chipFiltro` e `.chip*` | Chip.jsx/.css; BarraBalcao.jsx:35-51,140-149; TagsDoCliente.jsx:76-170; CartaoDoCliente.jsx:40-45; ModalProgramarLembrete.jsx:137; caixa.module.css:202-226; cliente.module.css:129-158 | Médio |
| R13 | P2 | `Vazio variante="linha"` + `Aviso embutido` para "Nenhum…": absorver `VazioEmLinha`, `.vazio` e ≈14 `<p>` crus | Vazio.jsx; PainelConversas.jsx:48-55,142-163; SeletorRespostas.jsx:49; Sininho.jsx:302; BlocoPedido.jsx:220-239 | Baixo |
| R14 | P2 | `Modal` com preset de confirmação (Cancelar + verbo), larguras `sm/md/lg`, X como BotaoIcone; padronizar rótulos Fechar/Cancelar/Voltar | Modal.jsx/.css; BarraModo.jsx:90-107; ModalBloqueio.jsx; SeletorJanela.jsx:77; PassarConversa.jsx:70; 7 larguras de 2.10 | Médio |
| R15 | P2 | Composer: Enviar e microfone no mesmo slot e mesmo tamanho (ícone só, ou ambos com rótulo); estado do interruptor "Sugestões de IA" visível (`[aria-pressed]` no Botao); apagar regras de `.acoes > label/acaoComRotulo/chaveLigada` | Composer.jsx:379-417; atendimento.module.css:285,304-317,342-354,393-399,491-504; Botao.module.css | Médio |
| R16 | P2 | Corrigir referências quebradas: criar/remover `.concluido`, `.freteCliente`; `--info-fundo` -> `--info-pastel` | BarraProximoPasso.jsx:514; CartaoDoCliente.jsx:36,37; cliente.module.css:284 | Baixo (mudança visual pequena) |
| R17 | P2 | Raio e ponto só por token: trocar `50%`, `999px`, `8px`, `2px`; decidir se os 5 tokens de raio continuam 0 | atendimento.module.css:97,442; composerAnexos.module.css:28,39,57; caixa.module.css:120; ficha.module.css:51,493; cliente.module.css:213,274; bolhaAnexo.module.css:33; tokens.css:135-139 | Baixo; decisão visual do dono |
| R18 | P2 | Espaço na grade de 4: remover 85 literais (2/3/5/6/7/9/10px); se quiser micro-gap, 1 token de 2px | lista 4.5 (moldura 7 pontos, atendimento 14, caixa 9, ficha 8, cliente 7…) | Médio: cada px move layout |
| R19 | P2 | Elevação por camada: menu/popover/tooltip/modal `--sombra-media`; gaveta e painel flutuante `--sombra-alta`; tirar borda e divisórias 1px do painel do Assistente | atendimento.module.css:38,255; lembretes.module.css:38; cliente.module.css:85; assistente.module.css:46,76,92,115,148 | Baixo |
| R20 | P2 | Vocabulário da ficha: "Ficha" só a coluna, modal = "Cadastro do cliente", "Resumo" só fechamento, `parte="Resumo"` -> "Ficha"; Comanda/Pedido e Cardápio/Link por decisão do dono | Moldura.jsx:584,605,624; ModalCliente.jsx:55,61; PainelFicha.jsx:158,207,220,245,263,275,280; ComandaDoCliente.jsx:291; CompartilharComanda.jsx:27; ModalEnviarCardapio.jsx:60,90 | Baixo; pedir decisão (múltipla escolha) |
| R21 | P2 | Apagar CSS morto: 37 órfãs, 5 `modo_*`, regras que não casam, declarações sobrescritas, `Selo` ou adotá-lo (R05), ramo `Coluna` com título | lista da seção 5 | Baixo; rodar `npm run contraste` e as provas visuais |
| R22 | P3 | `Botao` ganha `variante="link"` e `variante="item"` e prop `ocupado`/`dica`; cartão-botão e item de menu deixam de ser `<button>` cru; `Botao` do topo na mesma variante (hoje Cozinha/Ajuda `texto`, Automáticas/Gestão/Mais `secundario`) | Botao.jsx/.css; Moldura.jsx:234,241,253,272,134; lista 2.3 | Médio |
| R23 | P3 | Camadas flutuantes sobre `Popover`: Sininho, MenuDaConta, Disposição, Gaveta (Esc, clique fora, posição) | Sininho.jsx:120-215; MenuDaConta.jsx:40-64; Moldura.jsx:285-305,381-405 | Alto |
| R24 | P3 | `Campo`: `CampoBusca`, `CampoCaixa`, `CampoIntervalo`; os 2 `<select>` -> `CampoSelecao` | Campo.jsx; BarraBalcao.jsx:123-139,176; SeletorRespostas.jsx:30; 6 checkboxes de 2.13; Moldura.jsx:295,298 | Médio |
| R25 | P3 | `Avatar tipo="assistente"` e tile de ícone nos tamanhos da escala (32/40/48); `BarraDeAbas variante="segmentos"` e CSS de 3 camadas -> 1; aliases de `Icone` e tokens duplicados | Avatar.jsx/.css; AvatarAssistente.jsx; BalaoAssistente.jsx:258,274; AreaColaborativaIa.jsx:97; BlocoCliente.jsx:276; BarraDeAbas.jsx/.css; Icone.jsx:44,79,83,124,143,167; tokens.css:46-47,106-107,130-132 | Baixo |

DECISÕES QUE SÓ O DONO PODE TOMAR (para perguntar em múltipla escolha)
1. Comanda ou Pedido como palavra única da ficha. 2. Cardápio ou Link como nome do objeto enviado. 3. Raio: tudo reto (trocar literais) ou devolver valor aos tokens. 4. "Atendente" ou "Responsável". 5. "Automações" ou outro nome para as regras do topo.

LIMITES DA MEDIÇÃO
- Estático: não rodei a tela, não medi pixel real, não conferi a cascata por navegador. Onde digo "morto", "sem efeito" ou "não casa", foi por ordem no arquivo, especificidade e estrutura do JSX.
- Classes usadas por montagem dinâmica (`css['canal_'+x]`, `css['estado_'+x]`, `css[situacao.chave]`, `css[ocupacao.chave]`, `css[lado]`, `css[tom]`) foram tratadas como vivas; classe passada por parâmetro (`classesDoCartao(css,…)`) também.
- Contagens de `<button>`, `<details>`, `role="alert"` e `title=` incluem só as telas pedidas (app/Moldura.jsx, atendimento, caixa-de-entrada, ficha-cliente, notas, assistente, encerramento, lembretes); `componentes/` entrou nas contagens de CSS.
- Não verifiquei o que faz `ResumoEntregas`, `SinoDaSessao`, `PainelAgente` além do que toca nas telas.
- Custo: não houve subagentes; nenhum token medido para relatar.