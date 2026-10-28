# Banca UX, parte B: o Balcão na tela real

Data: 2026-10-28. Código: master `c59b696e5` (worktree `ux-balcao-leitura`). Sem servidor, sem edição de código, nenhum processo morto.

## Como foi medido

| Item | Valor |
|---|---|
| Build demo (dados fictícios embutidos) | `build-demo`, Playwright 1.62.1 + chromium, `serviceWorkers: block` |
| Build API (somente leitura) | `build-api` + API de laboratório `127.0.0.1:18816`, login com usuário do seed. Dados do laboratório são de outra sessão (17 conversas "BANCA-C"); não enviei, criei nem alterei nada nela |
| Larguras | 390x844 (celular, toque), 1280x720, 1920x1080 |
| Medição | injeção de JS no DOM (`tools/medir.js`): elementos interativos visíveis com hit-test (`elementFromPoint`), botões só-ícone, `title`/`aria-label`, alvo < 44 px, texto < 12 px, sobreposição, pílulas por cor de fundo, animações via `document.getAnimations()` |
| Ferramentas | `...\B-tela-real\tools\*.js`; resultados brutos em `m1-inicial.json`, `m2-matriz.json`, `m3-movimento.json`, `m4-api.json` |

Legenda: **MEDIDO** = número lido no DOM. **IMPRESSÃO** = leitura minha da captura, sem número. Dados do demo mostram o que a UI faz; o conteúdo (por exemplo, qualidade da sugestão do assistente) é simulado e não foi julgado.

Limite honesto: não testei o APK real, teclado virtual do celular, gesto de arrastar, botão voltar do Android nem som. O que depende disso está marcado.

## 1. Medições por largura (demo, estado inicial, conversa José Moretti aberta)

| Medida | 390 | 1280 | 1920 | API 390 / 1280 / 1920 |
|---|---|---|---|---|
| Interativos visíveis (hit-test) | 24 | 42 | 48 | 24 / 45 / 52 |
| Só-ícone sem texto | 3 | 3 | 3 | 3 / 3 / 3 |
| Só-ícone sem `aria-label` nem `title` | 0 | 0 | 0 | 0 / 0 / 0 |
| Alvos < 44 px (altura ou largura) | 6 | 22 | 20 | 6 / 25 / 24 |
| Alvos < 24 px | 0 | 5 | 5 | 0 / 6 / 7 |
| Texto < 12 px (fonte > 0) | 0 | 0 | 0 | 0 |
| Menor fonte visível | 13 px | 13 px | 13 px | 13 px |
| Cores de fundo distintas em pílulas/badges | 5 | 6 | 6 | 4 / 3 / 3 |
| Sobreposição real entre interativos | 0 | 0 | 0 | 0 |
| Rolagem horizontal da página | não | não | não | não |
| Animações infinitas rodando | 5 | 6 | 6 | n/m |

Pontos positivos medidos: todo botão só-ícone da tela principal tem nome acessível; nenhum texto abaixo de 13 px; nenhuma rolagem horizontal; nenhuma sobreposição real (os 3 "sobre" do 390 inicial são cartões cortados pela barra inferior, efeito de rolagem).

Combinações de cor (texto/fundo) dos rótulos dentro de um cartão de conversa: **7** (marrom escuro, marrom sobre fundo marrom do contador, âmbar sobre amarelo, marrom do tempo, verde "Pago", cinza, vermelho sobre rosa). Sinais para o mesmo fato "cliente esperando": fundo rosa, barra vermelha à esquerda, piscar de 1600 ms, rótulo "Esperando você", contador marrom, e o chip "Precisa de você" (5 sinais mais o chip).

### Por estado (hit-test, `m2-matriz.json`)

| Estado | 390 int / <44 | 1280 int / <44 | 1920 int / <44 | PNG |
|---|---|---|---|---|
| A inicial | 24 / 6 | 42 / 22 | 48 / 20 | `40-{w}-A_inicial.png` |
| B conversa (Heloísa) | 21 / 10 | 42 / 22 | 48 / 20 | `40-{w}-B_conversa.png` |
| C cardápio aberto | 10 / 0 | 34 / 21 | 40 / 21 | `40-{w}-C_cardapio.png` |
| D assistente | 15 / 1 | 45 / 27 | 55 / 29 | `40-{w}-D_assistente.png` |
| E respostas prontas | 24 / 9 | 46 / 22 | 51 / 19 | `40-{w}-E_respostas.png` |
| F ficha (modal) | 16 / 6 | 15 / 15 | 18 / 18 | `40-{w}-F_ficha.png` |
| G encerrar (modal) | 6 / 1 | 6 / 5 | 6 / 5 | `40-{w}-G_encerrar.png` |
| H link de compra (modal) | 4 / 0 | 3 / 3 | 3 / 3 | `40-{w}-H_link.png` |

### Espaço útil para ler a conversa (área rolável de mensagens)

| Largura | Altura | % da viewport | Balões visíveis |
|---|---|---|---|
| 390 | 477 px | 56% | 5 |
| 1280 | 453 px | 63% | 5 |
| 1920 | 907 px | 84% | 9 |

Com o cardápio aberto no 1280, a coluna da conversa passa de 462 px para 335 px (-27%) e a lista de conversas desaparece (`06-adicionar-itens.png`).

### Movimento (`m3-movimento.json`)

| Ação | Animações | Duração |
|---|---|---|
| Trocar de conversa (1280) | 7 entradas de balão, 3 transições de cartão, 1 pop do contador | 220 / 150 / 320 ms |
| Abrir ficha (1280) | diálogo + contador | 220 / 320 ms |
| Aba Balcão para Atendimento (390) | 6 entradas + transições | 220 / 90 / 150 ms |
| Contínuas | 5 piscares de SLA (1600 ms) + 1 pulso no trilho (2400 ms) | infinito |

Transições são curtas (<= 320 ms), nada pesado. O ruído é o piscar contínuo de 5 cartões.

### Passar o mouse

Controles só-ícone testados no 1280: Encerrar, Programar, Nota interna, Enviar. Os 4 têm `title` nativo (atraso do navegador); **0 tooltips próprios** (`[role=tooltip]`). No celular o `title` não aparece (IMPRESSÃO: sem toque longo equivalente). O `title` de "Enviar" desabilitado diz "Chat do site não aceita foto, arquivo nem áudio", texto que descreve outro botão. Nenhum dos dois desabilitados (Programar, Enviar) muda opacidade (1,0 medido).

## 2. Tarefas da atendente

Gestos = cliques (desktop) ou toques (celular) até concluir, contando abrir a conversa. Rolagem e fechamento de janelas contados à parte.

| # | Tarefa | Desktop 1280 | Celular 390 | Onde hesitei / o que a tela não explica | Evidência |
|---|---|---|---|---|---|
| 1 | Achar a conversa que precisa de resposta | 0 para ver (lista já ordenada por urgência), 1 para abrir. Mas a tela abre em José Moretti, que é a 34ª linha (y=2935 px, fora da lista visível) | 0 para ver, 1 toque para abrir; nenhuma conversa aberta ao entrar | Chip "Precisa de você 11" lista 11, mas só 8 cartões vêm destacados (Otávio, Sandra e Fábio ficam sem marca). Aberta fora da fila, não há linha selecionada visível | `01-inicial-1280.png`, `61-filtro-precisa.png` (11 linhas, 8 destacadas) |
| 2 | Ler e responder; resposta pronta | Abrir (1) + clicar no campo (2) + digitar + Enter (3). Com pronta: abrir, Respostas, escolher, Enter = 4; com `/agra` + Enter = 3. Foco NÃO vai ao campo ao abrir (activeElement = cartão da lista, 1280 e 1920) | Abrir (1) + Respostas (2) + escolher (3) + enviar (4). Foco no título, não abre teclado | Seletor de respostas prontas mede 148 px de largura na conversa de site (Renan), 364 px no WhatsApp; títulos truncados ("A...", "Como finalizar e...", "Confirmar j...") | `03-respostas-aberto.png`, `04-heloisa-respostas.png`, `32-m-respostas.png` (292 px) |
| 3 | Ver quem é o cliente e o histórico | Linha "Cliente e histórico" (1) abre modal sobre tudo; rolar 317 px (870 vs 553) (2) + botão "Histórico · 6" (3) abre segundo modal. 3 gestos + 1 rolagem + 2 fechamentos. Clicar no nome do cabeçalho: nada acontece | Aba Resumo (1) + linha (2) + rolar + "Histórico" (3). No API mobile a linha fica coberta pelo bloco Andamento (y=440, área rolável 316 de 456 px) | WhatsApp: tocar no nome abre os dados do contato em 1 gesto. Aqui o nome não é alvo | `05-ficha-cliente.png`, `05-ficha-fim.png`, `05-historico.png`, `53-api-m-resumo-coberto.png` |
| 4 | Montar pedido e cobrar (link) | 6: abrir, Adicionar itens, "+" no prato, Gerar cobrança, Link de pagamento, Confirmar e gerar | 8: abrir, aba Resumo, Adicionar itens, "+", fechar cardápio, Gerar cobrança, Link de pagamento, Confirmar e gerar | Cardápio ocupa o lugar da lista. "Gerar cobrança" continua visível e abre uma pergunta duplicada embaixo ("Como o cliente vai pagar?"). O link vai ao cliente sozinho ao confirmar, sem prévia (mensagem aparece no fio) | `06-adicionar-itens.png`, `07-gerar-cobranca.png`, `07-cobranca-gerada.png`, `70-m-t4-*.png` |
| 5 | Enviar o cardápio | "Link de compra" (1) + "Gerar e enviar" (2) = 3 com a abertura. Fotos dos pratos é outra galeria: Fotos (1) + Enviar no prato (2) | idem, 3 | O botão não diz "cardápio". O diálogo explica "o cliente escolhe pelo link" mas não mostra o que o cliente vai ver. Duas rotas diferentes para "mandar o cardápio" | `08-link-de-compra.png`, `09-fotos.png`, `34-m-link-compra.png` |
| 6 | Passar a conversa / encerrar | Encerrar: ícone (1) + "Encerrar" no diálogo (2). Passar: SÓ no modo API; ícone mão (1) + nome (2). No demo não existe | Encerrar 2, Passar 2 (API). Botão "Encerrar" quebra em 2ª linha no diálogo | Encerrar abre um relatório tipo PDF (duração, receita, avaliação, marcos, exportar). O ícone "porta com seta" é o mesmo de "sair". "Passar para..." só ganha texto em 1920 (42x40 só ícone em 1280 e 390) | `12-encerrar.png`, `35-m-encerrar.png`, `51-api-passar.png` |
| 7 | Assistente de IA para algo útil | Assistente (1) + rolar até "Sugerir resposta" (2) + clicar (3) + "Usar no rascunho" (4) + Enter (5). Cobre a coluna da ficha no 1280 | 5 também; abre folha cheia com foco no campo (o teclado cobriria o conteúdo, IMPRESSÃO) | Primeira tela são 3 cartões (Perfil, Tom, Padrão) que repetem a ficha. Dois botões quase iguais: "Usar sugestão no rascunho" e "Usar no rascunho". Dica "Ctrl+Enter para perguntar" também no celular. Texto sugerido no demo é genérico e vem marcado "Assunto fora do roteiro" | `10-assistente.png`, `10-assistente-sugestao.png`, `36-m-assistente.png` |
| 8 | Achar configurações | "Gestão" (ícone de 4 quadrados) (1) + "Configurações" (2) | "Mais" (1) + Gestão (2) + Configurações (3) | Não há engrenagem nem palavra "Configurações" no nível 1. Dentro, "Configurações" abre Integrações de entrega (Lalamove), não as da loja. Respostas prontas ficam em Gestão > Atendimento. O botão "T" tem `title` "conta e sair" mas o menu só mostra Claro/Escuro | `11-gestao.png`, `11-configuracoes.png`, `11-conta.png`, `62-m-mais.png` |

## 3. Outros achados medidos

| Achado | Número | Evidência |
|---|---|---|
| Um toque em "Loja aberta" (32x44, só ícone, no topo do celular) fecha a loja na hora, sem diálogo de confirmação (demo; efeito real não testado) | 1 toque, 0 diálogos, 32 px de largura | `62-m-loja-toque.png` |
| Quem responde (Automático x pessoa) é invisível: 25 de 35 cartões trazem "Automático" só como texto 1x1 clipado (leitor de tela). Na conversa, o aviso "Automático, responde sozinho até você assumir" + botão Assumir fica no topo do fio e rola para fora: y=-228 (Renan) e y=-2729 (Heloísa) no 1280; -219 no 390; visível só no 1920 em conversa curta. Mesmo assim o campo envia sem assumir | 25/35 | `02-renan-aberto.png`, `40-1920-C_cardapio.png` |
| Botão de fechar o cardápio sem nome acessível (sem `aria-label`, `title` nem texto) | 1 em 390, 1280 e 1920 | `70-m-t4-cardapio.png` |
| Faixa de filtros de canal cortada no 1280: Instagram (x 419-470) e Chat do site (x 476-527) passam da borda da lista (419) | 2 de 3 chips cortados | `01-inicial-1280.png` |
| Histórico do navegador não registra abrir conversa nem trocar de aba: `history.length` 2 antes e depois; `goBack` sai da página. O JS do Console não tem `popstate` nem `backButton` (grep). Como o APK trata o voltar nativo (há `WorkspaceActivity.java`) não foi testado | 0 entradas novas | `31-m-apos-back.png` |
| Celular, conversa aberta: 10 de 21 interativos < 44 px. Composer: Respostas, Fotos, Programar, Nota 36x36; campo 36 px de altura; microfone 40x40; chips de filtro 32 px de altura; link "automática" 81x19 | 10/21 | `30-m-conversa.png`, `40-390-B_conversa.png` |
| Celular, antes da primeira mensagem: barra superior com 6 a 7 alvos (módulos, Loja, Lembretes, [Avisos], Cozinha, Auto, Mais) mais subcabeçalho de 3 a 4 botões (132 a 176 px). Área de mensagens fica com 56% da tela | 7 alvos no topo (API) | `52-api-m-conversa.png` |
| Modo API celular: barra escura "4 canhotos na fila / Imprimir" fixa (aprox. 60 px) em todas as abas, sem relação com a conversa | 1 barra | `52-api-m-resumo.png` |
| Avisos (sino) no modo API: 71 não lidos, mostra 10, mistura validade de estoque ("d0b8592d vence em 5 dia(s)") com contato de cliente | 71 | `51-api-avisos.png` |
| Filtro "Precisa de você" tira da lista a conversa que está aberta; ela continua no fio ao lado sem nenhuma linha marcada | 1 | `61-filtro-precisa.png` |

## 4. Comparação com WhatsApp, Gmail, Android

| Ação | WhatsApp/Gmail | Balcão (medido) |
|---|---|---|
| Abrir conversa e já poder digitar | 1 gesto, foco no campo | 2 gestos (foco fica na lista no desktop) |
| Dados do contato | 1 toque no nome | 3 gestos + rolagem + 2 fechamentos; nome sem ação |
| Mandar catálogo | 1 menu de anexo, 2 gestos | "Link de compra" (nome diferente), 3 gestos |
| Voltar da conversa (Android) | tecla voltar | histórico não registra a troca (voltar sai da página, só no navegador; APK não testado) |
| Silenciar/fechar loja | confirmação | 1 toque sem confirmação (demo) |
| Quem atende | ícone/nome fixo no cabeçalho | aviso rola para fora do campo de visão |

## 5. Impressões (sem número)

- Pontos fortes: ordenação por urgência, chips de canal com contagem, texto de 13 px ou mais, rótulos acessíveis em todos os ícones, pedido criado sem sair da conversa, resposta pronta por `/`.
- O desktop de 1280 é denso para o tamanho do trabalho: lista, conversa e ficha com a ficha mostrando nota truncada ("Tirar da campanha de lasanha e de qualquer..." cortada) e a área de leitura com 5 balões.
- Mais de um caminho para a mesma coisa (cardápio: Adicionar itens, Link de compra, Fotos; cobrança: Gerar cobrança e Mandar link novo) sem diferença visível de intenção.

## 6. Arquivos

PNGs e JSON em `C:\Users\felip\AppData\Local\Temp\claude\C--rep-EasyStok\a6d84491-d750-4b77-b077-e39236d5972d\scratchpad\banca\B-tela-real\`. Builds em `...\scratchpad\build-demo` e `build-api`. Junção criada em `ux-balcao-leitura\EasyStock.Console\node_modules` (aponta para o checkout principal; não versionada).
