# Banca de UX do Balcão: pesquisa externa (frente C)

Data da pesquisa: 2026-10-28. Somente leitura, sem código do repositório lido.

## 0. Como ler e limites da evidência

| Item | Situação |
|---|---|
| m3.material.io | Páginas renderizadas em JavaScript: o fetch devolveu só o título. Valores de movimento, transição e barra de navegação foram confirmados no espelho oficial `material-components-android` (GitHub) e em developer.android.com. Regras de tooltip do M3 vieram de resumos de busca da especificação, sem os atrasos oficiais. |
| faq.whatsapp.com | Corpo das páginas truncado no fetch. Fatos do WhatsApp vêm de snippets de busca dessas mesmas páginas, do blog oficial (filtros) e de whatsappbusiness.com. |
| Gestores iFood, Goomer, Saipos, Anota AI, Consumer | Quase tudo é material de terceiros (Reportei, Saipos, ajuda.goomer) ou integradores. Nada de captura de tela oficial foi lido. |
| Kommo, Blip | Sem documentação oficial recuperada. Fora do relatório, exceto onde indicado. |
| Nubank, Itaú | Sem guia de escrita público localizado. Usei Apple HIG, Material, Mailchimp. |
| SF Symbols | Nomes dados de memória (não há fonte pesquisável no turno). Conferir no app SF Symbols antes de usar. |
| Gestos do WhatsApp (deslizar para responder, toque longo para selecionar) | Não confirmados em página oficial neste turno. Conhecimento geral, marcado como tal. |

Nenhum número abaixo foi inventado; onde a fonte não deu o valor, está escrito "não confirmado".

---

## 1. Anatomia do WhatsApp (e do Business)

### 1.1 Lista de conversas

| Elemento | O que é | Fonte | 1 gesto? | Aplicar ao Balcão |
|---|---|---|---|---|
| Filtros no topo (Todas, Não lidas, Grupos; depois Favoritas e Listas personalizadas) | Chips acima da lista. Lançados em abr/2024; "Não lidas" inclui conversas marcadas como não lidas ou nunca abertas | https://blog.whatsapp.com/find-messages-faster-with-chat-filters ; https://blog.whatsapp.com/focus-on-what-matters-with-custom-lists | 1 toque | Chips fixos: Todas, Não lidas, Minhas, Aguardando pagamento. Máximo 4 a 5 chips visíveis. |
| Ordem, prévia, horário, contador | Mais recente em cima; prévia de uma linha; horário à direita; bolinha verde com número | Padrão do produto (conhecimento geral, não confirmado em FAQ neste turno) | n/a | Mesma ordem. Prévia deve mostrar a ÚLTIMA fala do cliente, não do sistema. |
| Fixar conversa | Fixa no topo | FAQ "How to pin or unpin a chat" (título listado, corpo não lido) | Toque longo / menu | Fixar com `push_pin`; limitar a 3 como o WhatsApp (limite do WhatsApp não confirmado aqui). |
| Arquivar | Tira da lista principal; fica em link "Arquivadas" no topo; não notifica, exceto menção ou resposta | https://faq.whatsapp.com/web/chats/how-to-archive-or-unarchive-a-chat-or-group | Menu (web: passar o mouse e abrir o menu) | Equivale a "Resolvido". Reabrir sozinho quando o cliente responde (convenção de inbox, seção 2). |
| Marcar como não lida | Só para você; o remetente continua vendo "lida" | https://faq.whatsapp.com/4387000164859366/?cms_platform=web | Menu | Útil para "voltar nisso depois". Deve existir. |

### 1.2 Conversa e composer

| Elemento | O que é | Fonte | Aplicar ao Balcão |
|---|---|---|---|
| Cabeçalho | Avatar, nome, status; toque abre dados do contato; menu de 3 pontos | Padrão do produto (conhecimento geral) | Cabeçalho = cliente + estado + 1 ação principal (Passar). O resto no menu `more_vert`. |
| Composer | Emoji à esquerda, campo, clipe, câmera; vira botão de áudio quando vazio e de envio quando há texto | Padrão do produto (conhecimento geral) | Botão único que alterna microfone/enviar conforme o campo. |
| Respostas rápidas | Digitar "/" abre as respostas salvas, inclusive as padrão (horário, endereço); limite de 50; atalho configurável | https://faq.whatsapp.com/1791149784551042/?cms_platform=android | "/" no composer é a convenção mais forte para o Balcão. Padrões automáticos: horário, endereço, chave Pix. |
| Enter envia | Opção "Enter envia" | https://faq.whatsapp.com/android/chats/how-to-configure-the-enter-key/ | No desktop, Enter envia e Shift+Enter quebra linha; configurável. |
| Deslizar para responder, toque longo para selecionar | Gestos de mensagem | NÃO confirmado em fonte oficial neste turno | Sempre ter equivalente visível (menu da mensagem). Ver NN/g sobre swipe (seção 5.6). |

### 1.3 WhatsApp Business (o que o dono já conhece)

| Recurso | Fato | Fonte |
|---|---|---|
| Etiquetas | Até 20; aplicar: deslizar a conversa, Mais, Etiquetar; vários rótulos aparecem lado a lado; filtrar por Etiquetas puxando a lista | https://faq.whatsapp.com/1006488139736642/?locale=en_US (via busca) |
| Saudação | Resposta automática a quem escreve primeiro ou volta após 14 dias; destinatários: todos, fora dos contatos, exceto selecionados | https://faq.whatsapp.com/501866148528310/ (via busca) |
| Ausência | Resposta automática fora do horário, com agenda própria (detalhe da agenda não confirmado) | https://whatsappbusiness.com/pt-br/resources/resource-library/small-business-use-whatsapp-business-app/ |
| Catálogo e carrinho | Cliente monta carrinho, toca em "Enviar pedido", o pedido chega como mensagem na conversa; o pedido só vale quando o vendedor confirma; cliente não paga no carrinho | https://faq.whatsapp.com/5495780857144067/?cms_platform=android ; https://faq.whatsapp.com/3569210309807042/?locale=en_US |
| Meta Business Agent (ex Business AI) | IA responde clientes com base em catálogo e chats antigos; o dono escolhe quando responde sozinha, pode reescrever respostas, testar como cliente e assumir qualquer conversa; o cliente vê aviso de que é IA; toque longo numa mensagem da IA dá feedback | https://faq.whatsapp.com/1153795669452207/?cms_platform=web ; https://whatsappbusiness.com/products/business-app-ai-agent/ |

Lição para o Balcão: o WhatsApp separa a IA que fala com o cliente (precisa de aviso ao cliente) de ferramentas do atendente. Seu Balcão é o segundo caso: rascunho para a atendente, nunca envio automático.

---

## 2. Inboxes compartilhadas: convenções que se repetem

| Padrão | Quem faz | Fonte | Aplicar ao Balcão |
|---|---|---|---|
| Estados: aberto, pendente (aguardando cliente), adiado/snooze, resolvido | Chatwoot (Aberta, Pendente, Adiada, Resolvida; reabre sozinha se o cliente responde); Help Scout (Ativa, Pendente, Fechada); Intercom (Aberta, Fechada, Adiada); Zendesk (Novo, Aberto, Pendente, Em espera, Resolvido) | https://developers.chatwoot.com/api-reference/conversations/toggle-status ; https://docs.helpscout.com/article/419-keyboard-shortcuts ; https://www.intercom.com/help/en/articles/6258745-the-inbox-explained ; https://support.zendesk.com/hc/en-us/articles/8263915942938 | Quatro estados bastam: Aberta, Aguardando cliente (inclui "aguardando pagamento"), Adiada, Resolvida. Reabrir ao responder. |
| Não atribuída por padrão | Intercom ("Unassigned"); Chatwoot atribui manual ou por política | https://www.intercom.com/help/en/articles/6258745-the-inbox-explained | Conversa nova sem dono, com chip "Sem dono". Com 1 a 3 atendentes, atribuir ao primeiro que responder. |
| Presença: ver quem está com a conversa aberta | Intercom mostra avatares dos colegas na linha | idem | Evita resposta duplicada, o maior risco com 3 atendentes. Mostrar avatar "Fulana está respondendo". |
| Atribuir / passar | Atalho `A` no Intercom e Help Scout; Chatwoot por menu de contexto e em lote | docs acima | Botão "Passar" no cabeçalho, com nota obrigatória opcional ("passando porque..."). |
| Nota interna distinta da resposta | Help Scout `N`; Chatwoot nota privada com @menção; Front comentários e discussões internas | https://docs.helpscout.com/article/419-keyboard-shortcuts ; https://help.front.com/en/articles/2098 | Nota com fundo diferente (amarelo claro) e ícone `sticky_note_2`; nunca confundível com mensagem ao cliente. |
| Painel lateral do cliente | Intercom: conversas recentes (3, até 20), conversas parecidas, duplicatas | https://www.intercom.com/help/en/articles/6258745-the-inbox-explained | Painel direito no desktop: últimos pedidos, endereço, preferências, pedido aberto. Em celular vira bottom sheet. |
| Atalhos de teclado e paleta | Help Scout: R responder, N nota, A atribuir, S+A/P/C status, `/` buscar, `?` lista de atalhos, Cmd/Ctrl+K paleta. Intercom: L alterna lista, A atribui, Cmd/Ctrl+K | https://docs.helpscout.com/article/419-keyboard-shortcuts ; Intercom acima | Adotar o mesmo vocabulário: R, N, A, `/`, `?`, Ctrl+K. |
| Ações em lote por seleção | Intercom, Chatwoot, Front (seleção longa no celular com Arquivar/Adiar) | https://help.front.com/en/articles/4907072 | Só quando a fila passar de ~10; no celular, toque longo seleciona. |
| Transferir com contexto | Material de plataformas de WhatsApp (atribuição manual, round-robin, handoff com histórico) | https://www.wati.io/pt-br/blog/atendimento-whatsapp-equipe/ (terceiro) | Ao passar, registrar quem passou e por quê; a próxima atendente vê isso no topo. |

Convenções que viraram padrão (aparecem em 3 ou mais produtos): lista à esquerda, conversa ao centro, contexto à direita; nota interna visualmente distinta; atalho `A` para atribuir; paleta Ctrl/Cmd+K; reabrir ao responder; estado "pendente" = bola com o cliente.

Cuidado conhecido: no Front, atribuir a outra pessoa arquivava a conversa para quem passou (comunidade antiga, comportamento pode ter mudado). Para o Balcão: ao passar, a conversa SAI da lista da atendente com aviso e botão Desfazer.

---

## 3. Gestores de pedido brasileiros

| Produto | O que mostra | Fonte | Aplicar ao Balcão |
|---|---|---|---|
| Gestor de Pedidos iFood | Pedido novo em tempo real com alerta sonoro e visual; botões simples: aceitar, em preparo, despachar; filtros por horário, status e pagamento; impressão automática para a cozinha; chat com o cliente; central de problemas para cancelado/duplicado/atrasado; abrir/fechar loja visível | https://reportei.com/gestor-de-pedidos-ifood/ ; https://saipos.com/ifood/iconnect-ifood (ambos terceiros) | A atendente já espera: som + faixa de destaque para pedido novo, aceite explícito, estados em sequência. |
| Integradores do iFood | Ações Aceitar, Cancelar, Despachar, Pronto para retirada; Concluído após confirmação ou 2 horas; opção de aceite automático | https://ajuda.alterdata.com.br/spice/pedidos-spice/pedido-ifood ; https://ajuda.tabletcloud.com.br/integracoes/integracao-com-ifood-o-que-preciso-saber | Vocabulário: Aceitar, Em preparo, Pronto, Saiu para entrega, Concluído. |
| Saipos Kanban | Colunas por etapa (cozinha, aguardando entrega, saiu para entrega), personalizáveis; mostra horário de entrada, tempo em cada etapa, previsão de entrega | https://saipos.com/sistema/kanban-saipos | Cronômetro por etapa e ordem por previsão. Kanban só na tela da cozinha/expedição, não no Balcão. |
| Goomer | Etapa "Aceito" pode ser desligada; pedidos recusados num ícone no canto superior direito; histórico não editável | https://ajuda.goomer.com.br/goomergo/painel/pedidos/opcoes/painel-de-pedidos ; https://ajuda.goomer.com.br/goomergo/painel/pedidos/gestor-de-pedidos/pedidos-recusados | Etapas opcionais por loja; recusados fora do caminho principal, mas acessíveis. |
| Anota AI | Robô de WhatsApp + cardápio + gestor central (comprada pelo iFood); visão multiunidade | https://saipos.com/cardapio-digital/anota-ai ; https://mercadoeconsumo.com.br/22/09/2023/foodservice/ifood-se-une-a-anota-ai-e-cria-tecnologia-para-automatizar-restaurantes/ | Referência de "pedido sai da conversa". Detalhes de tela não confirmados. |
| Consumer | Nada relevante recuperado | n/a | Lacuna. |

O que a atendente de confeitaria já conhece: alerta sonoro, botão grande Aceitar, linha do tempo por etapas, impressão automática de comanda, horário de entrada visível.

Diferença de confeitaria: pedido sob encomenda tem data/hora de retirada, não "tempo de preparo". O destaque deve ser a data de entrega, não o minuto.

---

## 4. IA como copiloto em atendimento

| Produto | Padrão | Fonte | Aplicar ao Balcão |
|---|---|---|---|
| Intercom Copilot | Painel lateral direito; resposta direta com fontes; passar o mouse na fonte mostra resumo; aviso ao inserir resposta baseada em artigo interno; "Adicionar ao compositor" (rascunho, não envia); ajustar tom (mais amigável, formal), reformular, expandir, traduzir, adicionar como nota; salvar como macro; avaliação com 3 reações; atalho Ctrl/Cmd+K | https://www.intercom.com/help/en/articles/8587194-how-to-use-copilot | Padrão modelo: IA escreve NO composer, nunca envia; sempre mostra a fonte (cardápio, pedido, política). |
| Zendesk auto assist | Sugestões no lugar do composer; o agente revisa e aprova antes de enviar; ações só se definidas em procedimento; ações aprovadas por admin ficam no log; resumo de ticket com teto de 100 palavras (problema, ações, status); a Zendesk vai descontinuar "primeira resposta sugerida" em 19/01/2027, substituída por auto assist | https://support.zendesk.com/hc/en-us/articles/9945148867866-About-auto-assist ; https://support.zendesk.com/hc/en-us/articles/8037565521946 ; https://support.zendesk.com/hc/en-us/articles/7041677653914 | Resumo curto no topo da conversa (≤3 linhas). Ação sugerida só dentro de um conjunto fechado (criar pedido, gerar link, passar). |
| Gmail / Gemini | "Help me write" no composer, com refinar ou recriar; resumo no painel lateral; rascunho editável antes de enviar | https://support.google.com/mail/answer/14199860 | Botão de IA no composer e regeneração em um toque. |
| Shopify Sidekick | Nunca altera a loja sem aprovação; abre o editor com campos propostos destacados (roxo); você revisa e clica Atualizar; apps de terceiros pedem aprovação; trabalho longo roda em segundo plano e avisa; não conversa com cliente | https://help.shopify.com/en/manual/ai-powered-tools/sidekick/pulse ; https://help.shopify.com/en/manual/shopify-admin/productivity-tools/sidekick/set-up | Modelo para extração de pedido: a IA PREENCHE o rascunho do pedido com campos destacados; atendente confere e confirma. Nunca cria pedido nem cobra sozinha. |
| WhatsApp Meta Business Agent | Aviso ao cliente de que é IA; dono pode assumir; feedback por toque longo | https://faq.whatsapp.com/1153795669452207/?cms_platform=web | Se algum dia a IA falar com o cliente, rotular. Hoje é só rascunho interno. |
| Apple HIG (IA generativa) | Informar onde há IA; não fazer parecer humano; avisar que pode errar; pedir confirmação antes de ação significativa e não automatizar o difícil de desfazer (compras); feedback voluntário com polegar; controles Editar/Desfazer/Refazer junto do resultado; versões alternativas; opção sem IA | https://developer.apple.com/tutorials/data/design/human-interface-guidelines/generative-ai.json (página HIG "Generative AI") | Checklist direto da IA do Balcão. |
| Microsoft HAX | 18 diretrizes de interação humano-IA em 4 momentos (início, durante, quando erra, ao longo do tempo) | https://www.microsoft.com/en-us/research/publication/guidelines-for-human-ai-interaction/ | Usar como checklist de revisão (cobrir "quando erra"). |

Convenção de sinalização: ícone sparkle de quatro pontas é o marcador dominante de IA (Gemini, Fast Company); Material Symbols oferece `auto_awesome`. O sistema Spark da Genesys restringe o sparkle a funções de IA e sugere, para versões "IA" de ícones existentes, somar um sparkle ao canto aberto do ícone (https://spark.genesys.com/latest/foundations/ai-foundations/ai-iconography-eHo6ir4h-eHo6ir4h ; https://github.com/google/material-design-icons/issues/1697).

O que deu certo: rascunho no composer, fontes visíveis, ação com aprovação, resumo curto, ajuste de tom em um toque, feedback leve.
O que deu errado/risco: documentação do Intercom conflita sobre se o Copilot resume a conversa (usar função de resumo separada); Zendesk afirma que o cliente não percebe o auto assist, sem rótulo explícito de IA para o cliente; HIG alerta para alucinação em datas e dados, que é exatamente o risco em data de entrega, valor e endereço do pedido.

Como aplicar ao Balcão (regra 5 linhas):
1. IA nunca envia: escreve no composer (rascunho) com selo `auto_awesome` "Rascunho da IA".
2. Todo dado extraído (data, valor, endereço, itens) aparece em campo destacado com a frase do cliente que o originou.
3. Ação com dinheiro ou envio (gerar link, confirmar pedido) = botão da atendente, nunca automática.
4. Regerar e Descartar sempre a um toque; polegar para feedback.
5. Se a confiança é baixa ou falta dado: perguntar em vez de preencher (ex.: "Falta a data de retirada").

---

## 5. Sistema de convenções para aplicar

### 5.1 Dicionário de ícones

Material Symbols: nomes marcados (v) foram confirmados no repositório oficial google/material-design-icons (arquivo SVG existente) neste turno. SF Symbols: de memória, conferir no app.

| Conceito | Material Symbol | SF Symbol (conferir) | Nota |
|---|---|---|---|
| Configurações | `settings` (v) | `gearshape` | Engrenagem, só para configuração. Pedido explícito do dono. |
| Buscar | `search` (v) | `magnifyingglass` | |
| Filtrar | `filter_list` (v) ou `tune` (v) | `line.3.horizontal.decrease.circle` | |
| Voltar | `arrow_back` (v) | `chevron.backward` | Android: seta; iOS: chevron. |
| Fechar | `close` (v) | `xmark` | |
| Mais opções | `more_vert` (v) | `ellipsis` | Menu de 3 pontos vertical no Android. |
| Anexar | `attach_file` (v) | `paperclip` | Clipe, como no WhatsApp. |
| Câmera | `photo_camera` (v) | `camera` | |
| Enviar | `send` (v) | `paperplane.fill` | |
| Áudio | `mic` (v) | `mic.fill` | |
| Emoji | `add_reaction` (v) | `face.smiling` | |
| Resposta rápida | `quick_phrases` (v) ou `bolt` (v) | `bolt` | Aberto por "/". |
| Fixar | `push_pin` (v) | `pin` | |
| Arquivar / resolver | `archive` (v) ou `task_alt` (v) | `archivebox` | |
| Marcar não lida | `mark_chat_unread` (v) | `envelope.badge` | |
| Atribuir / passar | `person_add` (v) ou `assignment_ind` (v) | `person.badge.plus` | Para "Passar para", `forward_to_inbox` (v) também serve. |
| Adiar | `snooze` (v) ou `schedule` (v) | `moon.zzz` / `clock` | |
| Nota interna | `sticky_note_2` (v) | `note.text` | |
| Cliente / perfil | `account_circle` (v) | `person.crop.circle` | |
| Pedido | `receipt_long` (v) | `list.clipboard` | |
| Carrinho | `shopping_cart` (v) | `cart` | |
| Sacola / retirada | `shopping_bag` (v), `takeout_dining` (v) | `bag` | |
| Pagamento (cartão) | `credit_card` (v) ou `payments` (v) | `creditcard` | |
| Pix | NÃO existe em Material Symbols (`pix` retornou 404 no repositório). Usar símbolo oficial do Banco Central, sem redesenhar | n/a | Manual de uso da marca Pix permite o símbolo sozinho como botão em ambiente digital; não redesenhar: https://www.bcb.gov.br/content/estabilidadefinanceira/pix/Regulamento_Pix/I_manual_uso_marca_pix.pdf |
| Copia e cola / QR | `qr_code_2` (v) | `qrcode` | |
| Link | `link` (v) | `link` | |
| Entrega | `two_wheeler` (v), `moped` (v), `local_shipping` (v) | `shippingbox` | `delivery_dining` não achado no repositório; não usar. |
| Retirada na loja | `storefront` (v) | `storefront` | |
| Cozinha | `skillet` (v), `cooking` (v) | `fork.knife` | |
| Bolo / produto | `cake` (v), `bakery_dining` (v) | `birthday.cake` | |
| Impressora | `print` (v) | `printer` | |
| Notificação | `notifications` (v) | `bell` | |
| Ajuda | `help` (v) | `questionmark.circle` | |
| IA / assistente | `auto_awesome` (v) | `sparkles` | Sparkle de quatro pontas é a convenção atual de IA. |
| Desfazer | `undo` (v) | `arrow.uturn.backward` | |
| Copiar | `content_copy` (v) | `doc.on.doc` | |
| Responder mensagem | `reply` (v) | `arrowshape.turn.up.left` | |
| Etiqueta | `label` (v) | `tag` | |
| Concluído | `check_circle` (v), `done_all` (v) | `checkmark.circle` | |
| Painel de contexto | `right_panel_open` (v), `dock_to_right` (v) | `sidebar.right` | |
| Atalho de teclado | `keyboard_command_key` (v) | `command` | |

Regras de uso: um conceito, um ícone, em todo o produto (consistência interna, NN/g: https://www.nngroup.com/articles/usability-heuristics-complex-applications/); ícone sozinho só para os universais (fechar, voltar, mais, buscar, enviar, anexar); o resto leva rótulo (HIG tab bar pede rótulo em todas as abas: https://developer.apple.com/tutorials/data/design/human-interface-guidelines/tab-bars.json); `contentDescription` em todo ícone interativo (https://developer.android.com/guide/topics/ui/accessibility/principles).

### 5.2 Tooltips

| Regra | Material 3 | Apple HIG | Fonte |
|---|---|---|---|
| Quando | Plain: nomear ícone sem rótulo. Rich: explicação curta com ação. Plain aparece no hover (desktop) e no toque longo (mobile); rich no hover ou foco | Recomendado em macOS e visionOS; para iOS/iPadOS, sem orientação adicional (não existe hover) | https://developer.android.com/develop/ui/compose/components/tooltip ; https://developer.apple.com/tutorials/data/design/human-interface-guidelines/offering-help.json |
| Atraso | Valor oficial não confirmado neste turno (um espelho de terceiros cita 500 ms ao passar e 0 ms no foco: não oficial) | HIG não especifica | https://kds.koder.dev/pt-BR/reference/components-tooltips.html (terceiro) |
| Conteúdo | Curto; plain com largura padrão 200 dp, rich 320 dp | Descrever a ação do controle; começar com verbo; não repetir o nome do controle; 60 a 75 caracteres; sentence case; sem ponto final; pode mudar com o estado do controle | HIG acima |
| Atalho no tooltip | Boa prática em apps densos: tooltip com descrição + atalho | n/a | NN/g complex apps: https://www.nngroup.com/articles/usability-heuristics-complex-applications/ |
| Toque | Toque longo é permitido no M3 | Não é padrão no iOS | acima |

Regra proposta para o Balcão: tooltip só no desktop (hover e foco do teclado), com formato `Verbo + objeto` e o atalho à direita (ex.: "Passar conversa  A"). No celular, NUNCA depender de tooltip: ícone sem rótulo precisa de `contentDescription` e, quando importante, ganha rótulo visível. Informação essencial nunca fica só no tooltip (HIG).

### 5.3 Movimento

Tokens de duração (material-components-android, https://github.com/material-components/material-components-android/blob/master/docs/theming/Motion.md):

| Token | ms | Uso sugerido |
|---|---|---|
| short1 / 2 / 3 / 4 | 50 / 100 / 150 / 200 | Hover, ripple, chip, tooltip, toggle |
| medium1 / 2 / 3 / 4 | 250 / 300 / 350 / 400 | Menu, bottom sheet, snackbar, expandir painel |
| long1 / 2 / 3 / 4 | 450 / 500 / 550 / 600 | Transição de tela cheia |
| extraLong1 / 2 / 3 / 4 | 700 / 800 / 900 / 1000 | Evitar em UI de trabalho |

Easings: standard `cubic-bezier(0.2, 0, 0, 1)`; standard decelerate `(0, 0, 0, 1)` (entrada); standard accelerate `(0.3, 0, 1, 1)` (saída); emphasized decelerate `(0.05, 0.7, 0.1, 1)` (entrada de elementos grandes); emphasized accelerate `(0.3, 0, 0.8, 0.15)` (saída); emphasized é definido por path, sem cubic-bezier equivalente (usar standard como aproximação).

Padrões de transição (mesma fonte):

| Padrão | Quando | Balcão |
|---|---|---|
| Container transform | Elemento com contêiner vira a tela de detalhe (card vira página; FAB vira tela) | Linha da lista de pedidos expande para o painel do pedido; botão "+ Pedido" expande para o rascunho. |
| Shared axis | Relação espacial ou de navegação (X para irmãos, Y para passos, Z para pai-filho) | Abrir conversa a partir da lista no celular (Z ou X); passos de montar pedido (X). |
| Fade through | Destinos sem relação forte, como itens da barra inferior | Trocar entre Conversas, Pedidos, Cozinha. |
| Fade | Entrada e saída dentro da tela: diálogo, menu, snackbar, FAB | Menus, tooltip, snackbar de "Desfazer". |

Voltar preditivo no Android: o conteúdo diminui de 100% a 90% durante o gesto; troca por fade through no limiar de 35%; interpolador `(.1, .1, 0, 1)`; cancelamento volta ao estado original; exige opt-in do app (https://developer.android.com/design/ui/mobile/guides/patterns/predictive-back).

Acessibilidade: respeitar `prefers-reduced-motion: reduce`, trocando deslocamento e escala por fade (ou nenhum) (https://developer.mozilla.org/en-US/docs/Web/CSS/Reference/At-rules/@media/prefers-reduced-motion). Regra do Balcão: nada acima de `medium2` (300 ms) em interação frequente; nunca animar a chegada de mensagem de forma que atrase a leitura; sem animação infinita (a não ser o indicador de "digitando/IA pensando", e esse também desliga com reduce).

### 5.4 Copy (UX writing)

| Regra | Fonte | Exemplo para o Balcão |
|---|---|---|
| Botão com verbo e específico | Apple HIG writing: https://developer.apple.com/tutorials/data/design/human-interface-guidelines/writing.json ; Mailchimp: https://styleguide.mailchimp.com/web-elements/ | "Gerar link de pagamento", "Passar conversa", "Confirmar pedido". Não "OK", "Sim", "Enviar" solto. |
| Não culpar, dizer como corrigir; sem "Ops!" | HIG writing | "Falta a data de retirada. Informe para gerar o pedido." |
| Erro perto do problema e sem "nós" | HIG writing | "Não foi possível gerar o link. Tente de novo." |
| Estado vazio com próxima ação | HIG writing | "Nenhuma conversa aberta. Respostas novas aparecem aqui." (sem botão se nada a fazer) |
| Tom varia conforme a situação; humor repetido cansa | Material communication: https://codelabs.developers.google.com/codelabs/material-communication-guidance?hl=pt-br | Erro e dinheiro: sério. Estado vazio: leve. |
| "Você", frases curtas, informação essencial primeiro | Material communication; HIG writing | |
| Confirmação destrutiva: só para o irreversível, botão com o nome da ação, Cancelar presente, botão destrutivo só se não foi escolha deliberada | Apple HIG alertas: https://developer.apple.com/tutorials/data/design/human-interface-guidelines/alerts.json ; NN/g: https://www.nngroup.com/articles/confirmation-dialog/ | "Cancelar este pedido?" com botões "Cancelar pedido" e "Manter". |
| Desfazer em vez de "tem certeza?" para ação reversível e frequente | NN/g (swipe e confirmação): https://www.nngroup.com/articles/contextual-swipe/ | Arquivar, passar, descartar rascunho de IA: snackbar "Conversa passada para Ana. Desfazer" por ~6 segundos (duração da snackbar é decisão de produto, não vem do M3 pesquisado). |
| Rótulos de fluxo consistentes (Começar, Continuar, Concluído) | HIG writing | |
| Em configurações, descrever o que acontece quando ligado | HIG writing | "Avisar com som quando chegar pedido novo." |
| Nubank/Itaú | Não encontrado guia público | Lacuna. |

### 5.5 Redução de ruído

| Princípio | Fonte | Aplicação |
|---|---|---|
| Uma ação primária por tela, as demais secundárias | M3 (FAB como única ação principal): conhecimento geral, não reconfirmado | Composer: Enviar é a primária. Cabeçalho: Passar. Pedido: um único botão "Gerar link" ou "Confirmar", conforme o estado. |
| Divulgação progressiva: raros em nível secundário; remover ícones redundantes | NN/g complex apps: https://www.nngroup.com/articles/usability-heuristics-complex-applications/ | Menu de 3 pontos para: marcar não lida, etiqueta, imprimir, histórico. |
| Badge só para o crítico, vermelho com número ou ponto, para não diluir | Apple HIG tab bars: https://developer.apple.com/tutorials/data/design/human-interface-guidelines/tab-bars.json ; Material (badge em ícone ou com número): https://github.com/material-components/material-components-android/blob/master/docs/components/BottomNavigation.md | Badge só em "Não lidas" e "Aguardando você". Máximo 2 badges na navegação. Passou de 99, mostrar "99+". |
| Cor + forma/texto, nunca só cor | https://developer.android.com/guide/topics/ui/accessibility/principles | Status com ícone e rótulo, não só bolinha colorida. |
| Cores de status: poucas | Proposta (nenhuma fonte fixa o número) | Limite 4: neutro (aberta), âmbar (aguardando), verde (resolvida/pago), vermelho (problema). Etiquetas do WhatsApp Business passam de 20 rótulos e perdem valor (https://chatsac.com/blog/etiquetas-whatsapp-business/, terceiro). |
| Não esconder/mostrar botões de navegação; explicar vazio | HIG tab bars | Seção vazia continua visível, com texto. |
| Alerta modal só quando essencial | HIG alertas | Notificação de pedido novo = faixa + som, não diálogo. |
| Densidade | Lista compacta no desktop, confortável no celular; alvo de toque 48 dp no Android (valor consagrado do Material; a página de acessibilidade lida não o repetiu) | |

### 5.6 Navegação

Android e responsividade (https://developer.android.com/develop/ui/compose/layouts/adaptive/support-different-display-sizes):

| Largura | Classe | Navegação |
|---|---|---|
| < 600 dp | Compact | Barra inferior, 3 a 5 destinos |
| 600 a 839 dp | Medium | Navigation rail |
| ≥ 840 dp | Expanded | Rail ou drawer permanente |

Barra de navegação: 3 a 5 destinos, ícone + texto; com 4 ou mais, por padrão só o selecionado mostra rótulo (MDC); cada item com título para o TalkBack (https://github.com/material-components/material-components-android/blob/master/docs/components/BottomNavigation.md). A Apple pede rótulos em todas as abas e no máximo 5 no padrão (HIG tab bars). Recomendação: manter rótulos sempre visíveis (são só 3 a 4 destinos).

Layouts canônicos (https://developer.android.com/develop/ui/compose/layouts/adaptive/canonical-layouts):

| Layout | Comportamento | Balcão |
|---|---|---|
| List-detail | Expanded: lado a lado; Compact/Medium: um painel por vez, o detalhe substitui a lista e Voltar retorna; encolher a janela mantém o detalhe | Lista de conversas e conversa. É o desenho do WhatsApp Web. |
| Supporting pane | O secundário só faz sentido com o principal; Compact: abaixo ou em bottom sheet; Medium: 50/50; Expanded: ~70/30 | Painel do cliente e do pedido ao lado da conversa (70/30). No celular, bottom sheet. |
| Desktop 3 painéis | List-detail + supporting pane | Lista (esq.) | Conversa (centro) | Cliente/Pedido/IA (dir.). |

No celular: Voltar do sistema fecha primeiro bottom sheet/teclado, depois conversa, depois a lista; usar voltar preditivo (acima). FAB: apenas uma ação primária de criação (ex.: novo pedido), e só se não colidir com o composer; na tela de conversa, sem FAB. Top app bar: título + busca + `more_vert`; engrenagem de configurações fica no menu/rodapé de perfil, não na barra principal.

---

## 6. Heurísticas de Nielsen no cockpit e teste rápido

Fonte base: https://www.nngroup.com/articles/ten-usability-heuristics/ e https://www.nngroup.com/articles/usability-heuristics-complex-applications/.

| # | Heurística | No Balcão (o que verificar) |
|---|---|---|
| 1 | Visibilidade do estado | Quem está com a conversa; pago ou não; mensagem enviada/lida; IA pensando; link gerado e quando expira. |
| 2 | Correspondência com o mundo real | Linguagem de confeitaria: "encomenda", "retirada", "sinal", não "ordem de serviço". Ícones sem contradição (xícara de café ao lado de atendente disponível confunde). |
| 3 | Controle e liberdade | Desfazer passar/arquivar/descartar; cancelar link de pagamento; editar rascunho da IA. |
| 4 | Consistência e padrões | Um conceito, um ícone (seção 5.1); mesmo significado do "+" em toda a tela; seguir WhatsApp onde ele é padrão. |
| 5 | Prevenção de erros | Pré-visualização do pedido antes de cobrar; valor e data em destaque; confirmar só o irreversível; bloquear dupla resposta com presença. |
| 6 | Reconhecimento em vez de memória | Respostas rápidas por "/", itens recentes do cliente no painel, atalhos listados com `?`. |
| 7 | Flexibilidade e eficiência | Atalhos (R, N, A, `/`, Ctrl+K), gestos com equivalente visível, macros. |
| 8 | Estética minimalista | Uma ação primária, divulgação progressiva, no máximo 2 badges. |
| 9 | Erros claros | Mensagem de falha do Mercado Pago com causa e próxima ação. |
| 10 | Ajuda | Tooltips com atalho, estado vazio que ensina, tour curto reabrível (o Gestor iFood tem "Ajuda, Tour"). |

### Teste de corredor com 5 usuários (hallway test)

Base: 5 participantes por rodada encontram a maior parte dos problemas (curva com 31% por usuário, ~85% com 5); três rodadas de 5 com correção entre elas rendem mais que um estudo de 15; grupos de usuários diferentes pedem 3 a 4 por grupo (https://www.nngroup.com/articles/why-you-only-need-to-test-with-5-users/). Ressalva: dados do artigo são de 2000, ilustrativos; métricas quantitativas pedem ~20 usuários (mesma fonte). Para o Balcão, 5 = atendentes reais (2 a 3 da loja) mais 2 pessoas da família/operação com perfil parecido, e uma rodada de celular separada da de desktop.

Protocolo (30 minutos por pessoa):
1. Cenários, sem dizer onde clicar: (a) responder cliente com pergunta de preço; (b) montar encomenda de bolo com data; (c) gerar e enviar link Pix; (d) passar a conversa para a colega; (e) usar a IA para resumir e rascunhar; (f) achar a configuração de som.
2. Pensar em voz alta; o observador não ajuda.
3. Métricas por tarefa: tempo até concluir; taxa de sucesso sem ajuda; erros (toque errado, tela errada, envio duplicado); número de toques; pedidos de ajuda.
4. Ao fim, SUS de 10 itens (escala 1 a 5): ímpares contribuem resposta menos 1, pares contribuem 5 menos a resposta, soma × 2,5 dá 0 a 100; média de mercado ≈ 68 (percentil 50); não é percentual (https://measuringu.com/interpret-sus-score/ ; https://www.measuringu.com/sus/). Com 5 respondentes o SUS é indicativo, não prova.
5. Meta sugerida (proposta, não vem de fonte): responder uma pergunta simples em ≤ 15 s; gerar link em ≤ 40 s; zero envios duplicados; SUS ≥ 80 (acima da média).
6. Repetir após correção; registrar antes/depois por tarefa.

---

## 7. Lacunas honestas

- Atrasos oficiais do tooltip M3 e texto integral de m3.material.io não obtidos.
- Gestos do WhatsApp, limites de fixados e a lista de atalhos do WhatsApp Web não confirmados em FAQ oficial.
- Nenhuma tela real de Goomer, Consumer, Anota AI e Kommo/Blip foi analisada; somente descrição textual de terceiros.
- Não encontrei dado público sobre taxa de erro de IA em atendimento; a recomendação de "nunca enviar sozinha" vem das diretrizes da Apple e do padrão do Intercom e Zendesk (aprovar antes de enviar).
- Falta testar com atendentes reais; este relatório é pesquisa, não evidência de uso.
