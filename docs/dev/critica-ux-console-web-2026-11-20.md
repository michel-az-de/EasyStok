# Crítica de UI e UX do Console web (2026-11-20)

Método: dual-agent do Impeccable 4.10.0 (`critique`). 4 revisores de design por área (Início e navegação, Atendimento, Operação, Cadastros), 1 detector isolado (`impeccable detect` no fonte e por URL, medições Playwright, `npm run contraste`) e 4 refutadores, um por área, que tentaram derrubar cada achado contra o código, a tela e as issues.
Alvo: `EasyStock.Console` no master `52be7fb1a`, modo demonstração em `127.0.0.1:5880`, 1440x900 e 390x844, temas claro e escuro.
Não salvo em `.impeccable/critique/`: o alvo é o app inteiro, não uma tela.

## Nota: 27/40 (Aceitável, no limite do Bom)

| # | Heurística | Início | Atend. | Operação | Cadastros | Média | Problema principal |
|---|---|---|---|---|---|---|---|
| 1 | Estado do sistema | 3 | 3 | 3 | 3 | 3,0 | Cartões de módulo sem linha de estado no Início |
| 2 | Mundo real | 3 | 3 | 3 | 3 | 3,0 | "Em breve" e "Abre em outra janela" são jargão de implementação |
| 3 | Controle e liberdade | 3 | 3 | 3 | 3 | 3,0 | Dispensar aviso não sobrevive a recarregar |
| 4 | Consistência | 2 | 2 | 3 | 2 | 2,3 | Rail muda entre telas; tema em 3 lugares; 3 padrões de confirmação |
| 5 | Prevenção de erros | 3 | 3 | 3 | 3 | 3,0 | Bom: fechar loja e caixa confirmam, Desfazer com prazo |
| 6 | Reconhecer, não lembrar | 3 | 3 | 3 | 3 | 3,0 | Atalhos só na Ajuda |
| 7 | Eficiência | 3 | 3 | 2 | 2 | 2,5 | Sem atalho para a próxima conversa e para Novo/Salvar |
| 8 | Minimalismo | 2 | 3 | 2 | 3 | 2,5 | Faixa vermelha fixa; 19 chips de filtro em Entregas |
| 9 | Recuperação de erro | 2 | 2 | 2 | 3 | 2,3 | Aviso comum pintado como erro grave |
| 10 | Ajuda | 3 | 2 | 2 | 3 | 2,5 | Tokens crus `{linkCardapio}` e `{abre}` visíveis |
| | **Total** | **27** | **27** | **26** | **28** | **27/40** | |

## Especificidade de design

O conteúdo é da Casa da Baba: o Início organiza o dia por verbo (Responder, Preparar, Despachar), o vocabulário é o da loja (Esperando você, Diz que pagou, Restrição alimentar, Falta R$ 68,00) e o canhoto do pedido imita a comanda de papel. A casca é genérica: rail de ícones, cartões arredondados, tabela e modal de admin comum. Na superfície Operate isso é aceitável; a marca aparece no Balcão e some nas telas de ajuste.

Detector: **0 achados no fonte** (489 js, 252 jsx, 117 css). Por URL: 12 a 15 no Balcão (placeholder com contraste baixo, mais um falso positivo de padding na pílula), 1 a 2 em Respostas, Cozinha e Entregas (hierarquia tipográfica plana: corpo 14 a 15 px, h2 14 px, h1 17 px), 0 no Início e no Caixa. Overlay não injetado: execução headless, o navegador embutido está bloqueado.

## O que funciona de verdade

| O quê | Prova |
|---|---|
| Início organizado pelo caminho do pedido: Responder 11, Preparar 5, Despachar 1, com motivo e "Ver mais N" | `HallDeModulos.jsx:60`, captura 1440 |
| Urgência por cor e palavra, nunca só cor; restrição alimentar vem primeiro | Início e Balcão, 2 temas |
| Base de acessibilidade sólida: 0 botões sem nome, 0 imagens sem alt, foco visível 23/23 no Início e 24/25 no Balcão, nenhuma rolagem horizontal a 390 px, nenhum texto visível abaixo de 12 px, 264 pares de contraste aprovados nos 2 temas | Medição Playwright, `npm run contraste` |
| Busca de módulo e tela com resultado anunciado e estado vazio útil | Captura busca vazia |
| Cozinha: um botão por cartão com o verbo da próxima etapa e Desfazer com prazo em vez de "tem certeza?" | `CartaoKds.jsx:76-79` |
| Fechar caixa com prevenção real (contagem digitada, não reabre, loja não abre sem caixa) | Modal do Caixa |
| Janelas de entrega não deixam excluir o que tem pedido | Painel de janelas |
| Ficha com próximo passo e dinheiro em evidência ("R$ 68,00 a receber") | Ficha no Balcão |
| Cadastros inativam em vez de excluir; erro de formulário no campo, com foco no primeiro inválido; modal vira folha inferior no celular | `EditorCadastro.jsx`, `PainelCadastros.jsx` |
| Menu Mais do celular em folha inferior com alvos de 48 px e tema marcado | `menuMais.js`, medição 390 |

## O que não funciona (confirmado pelo refutador)

| Sev. | Problema | Onde | Correção | Comando |
|---|---|---|---|---|
| P2 | **Aviso de som e notificação em faixa vermelha de erro, fixa em toda tela**: 115 px no desktop e 140 a 160 px no celular (13 a 20% da altura). "Dispensar" vale só até recarregar. Achado por 3 revisores | `src/app/AvisosDoDispositivo.jsx:12-21` (`useState`), `faixaDeFalha.module.css` | Faixa neutra de 1 linha com "Ligar som"; lembrar a dispensa por aparelho; vermelho só para falha real | `/impeccable quieter` |
| P2 | **Cozinha no celular mostra 1 das 4 colunas** e esconde as contagens das outras; rolagem dupla | `cozinha.module.css:291-292` (coluna `100vw - 56px`) | Segmentos fixos "Na fila 3 · Em preparo 2 · Pronto 0 · Saiu 1" com uma coluna por vez | `/impeccable adapt` |
| P2 | **Navegação muda de tela para tela**: o rail do Balcão tem Lembretes, Ajuda, Disposição, Simular e avatar; o de Entregas não; o tema mora em 3 lugares (pé do Início, avatar, menu Mais) | Balcão e Entregas a 1440 | Um rail com a mesma lista e ordem em todas as rotas, menu de conta fixo | `/impeccable layout` (issue #1777 aberta cobre o tema geral) |
| P2 | **Início esconde os módulos**: cartões de Atendimento, Cozinha, Entregas e Financeiro só com o nome (114 px de altura, sem estado); a 390 px os módulos ficam a ~2.200 px de rolagem e não há menu nem tema no topo | `HallDeModulos.jsx:60-67` | Linha de estado em todo cartão ("Fila: 11 conversas", "Caixa aberto") ou faixa de 4 atalhos logo abaixo da busca | `/impeccable layout` |
| P2 | **Prévia das respostas prontas cortada**: 3,7 px de altura no desktop, só o título aparece | `atendimento/seletorRespostas.module.css:14-27` | Altura própria para `.previa`, `align-content: start` | `/impeccable layout` |
| P2 | **Cartão da conversa perde o selo de valor no desktop** ("Falta R$ 68,00" aparece a 390 e some a 1440 na coluna de 260 px) | `caixa-de-entrada/caixa.module.css:75-79` | Priorizar o selo de dinheiro ou combinar em um selo só | `/impeccable adapt` |
| P2 | **Placeholder "Buscar conversa" com contraste 3,1:1** (mínimo 4,5:1). Só o detector pegou; a prova de contraste do projeto não cobre placeholder nativo | `caixa.module.css:108`, sem `::placeholder` (o `Campo.module.css:39` já usa `--tinta-3`) | `.entradaBusca::placeholder { color: var(--tinta-3) }` | `/impeccable polish` |
| P2 | **Obrigatório e opcional marcados de jeitos opostos**: `*` em Cadastros, "(opcional)" em Insumos e Financeiro. Lido no código | `EditorCadastro.jsx:85,201` vs `AbaInsumosApi.jsx:214-217` | Um padrão no componente Campo | `/impeccable clarify` |
| P2 | **Carregar troca a lista inteira por "Carregando…"** em Formas de pagamento e Modelos, enquanto o painel de cadastros usa esqueleto. Lido no código | `MetodosCadastro.jsx:31`, `ModelosCadastro.jsx:79` | Manter a lista com `aria-busy` e usar o esqueleto só na primeira carga | `/impeccable polish` |
| P3 | Respostas automáticas: título repetido em h1, h2 e h3; tokens crus `{linkCardapio}` e `{abre}` na prévia | `#/m/atendimento/respostas` | Prévia com exemplo real; variáveis como selo | `/impeccable clarify` (issues #2783 e #2781 abertas tocam o tema) |
| P3 | Fidelidade e Produção do dia caem em "Esta página não existe" genérico | `modulos.js:103,127` | Página "Em preparo" que diz o que será | `/impeccable onboard` |
| P3 | Busca do Início empurra o título e duplica o "Limpar" | Início 1440 | Reservar a linha ou tirar o "Limpar busca" (o X já limpa) | `/impeccable polish` |

## Plausível, sem prova suficiente (P3)

- Cozinha sem leitura a distância: texto de 14 px e sem tempo decorrido no cartão (`tokens.css:107-108`).
- Entregas: 19 chips em 3 grupos (Mostrar, Ordenar, Bairro) e 4 linhas de filtro antes do primeiro cartão.
- Caixa aberto empilha saldo, totais e formulário de lançamento sem hierarquia de risco (não reproduzido na demo).
- 3 padrões para confirmar ação destrutiva: modal, `window.confirm` (`AbaFluxo.jsx:273`, `ModalAssistente.jsx:29`) e linha expandida.
- Linha de cliente no celular empilha até 4 botões; "Desativar" usuário com o mesmo peso de "Reenviar convite".
- Rótulos do rail com 11 px e quebra feia ("Configura-ções").
- Janela flutuante Revisar pedido cobre parte da conversa (as #2978 e #2979, fechadas hoje, já mexeram nela).

## Refutado no caminho

- "Aviso cobre as abas de baixo": a faixa é `position: static`; ela empurra o conteúdo, não sobrepõe.
- "Chaves de 20x20 px em Respostas abaixo de 24 px": o rótulo inteiro, com `min-height: var(--toque)`, aciona a chave (`automacoes.module.css:57-62`).
- "Compositor mistura estados": decisão documentada na #2258.
- `cramped-padding` na pílula de contagem do Balcão: falso positivo do detector.

## Personas

- **Atendente no pico, celular na mão, 8 conversas esperando**: a faixa vermelha toma ~125 px da tela; Pix colado empurra a pergunta da cliente para fora; na Cozinha só vê uma coluna.
- **Alex, dona veloz**: sem atalho para a busca, para a próxima conversa que precisa dela, nem para Novo ou Salvar; o rail muda e quebra a memória muscular.
- **Jordan, atendente nova**: "Em breve", "Abre em outra janela" e `{abre}` não dizem nada; obrigatório marcado de 2 jeitos.
- **Cozinheira a 2 m do monitor**: 14 px, sem tempo decorrido, quadro com rolagem interna.

## Lacunas

- Cadastros, Cardápio, Produção, Financeiro (exceto Caixa) e Configurações (exceto Integrações) exigem API real e **não foram vistos ao vivo**; avaliados só pelo código.
- Desfazer da Cozinha e estados de erro de API não foram provocados na demo.
- Relógio da demo é fixo; nota de desempenho (LCP, CLS) fora do escopo desta crítica.

## Perguntas para pensar

- O aviso de som precisa ficar visível o tempo todo, ou basta uma faixa discreta que some quando ela liga o som?
- A cozinha usa o Console em monitor fixo a distância? Se sim, vale um modo cozinha com tipografia grande e tempo decorrido.
- O Início deveria abrir direto em Conversas no celular da atendente?

## Errata (2026-11-21, auditoria FONTE_API)

Esta crítica foi medida no modo demonstração. A auditoria de 21/11 conferiu cada componente contra o build de produção (`VITE_FONTE_DADOS=api`, bundle `23722c8b4`), com 4 mapeadores e 4 refutadores Opus.

- **Caem:** P3 "Caixa aberto empilha" e "Entregas: 19 chips". Em produção essas telas são `AbaCaixaApi` e `ConteudoEntregasApi`, com 2 chips. A #3051 mexeu só na demonstração (#3042).
- **Cai pela metade:** "Início esconde os módulos". A linha de estado (`PulsoDoHall`) já aparecia em produção desde a #1851. Continua valendo a rolagem de ~2.200 px no celular.
- **Vale em outro alvo:** Cozinha no celular e Cozinha a distância. As PRs #3033 e #3046 mexeram no componente de produção (`TelaCozinhaApi`, `CartaoKds`).
- **Regra daqui em diante:** crítica de UX do Console mede o modo API (fixture), não a demonstração. Toda PR prova que o componente alterado entra com `FONTE_API`.
- **Caixa aberto, medido na `AbaCaixaApi` (21/11):** o lançamento abre em modal e o saldo "Na gaveta" tem destaque de 24 px. O "Fechar o caixa" em texto é decisão registrada (#2512, Q07): é ação rara, ao lado da ação do dia a dia.
- A diferença da gaveta só existe depois da contagem do fechamento, então com o caixa aberto não há o que avisar. Item ENCERRADO, sem PR e sem issue (gestão, 21/11 12:18).
