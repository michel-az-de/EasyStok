# Banca UX/UI do Atendimento (#2232)

Fonte: "Banca Revisora UX UI — Easystok · Atendimento" (PDF, 22 páginas), avaliada no celular a
558×684 px. Este documento é a spec S/P dos 45 achados: o que muda, o que já atende e o que fica
de fora, com o motivo.

## Decisões de base

| Tema | Decisão |
|---|---|
| Cantos | Seguem retos (PR #2143, aprovado em 29/10). Onde a banca pede canto arredondado, a correção vem pela geometria e pela cor, sem raio. A troca de direção é pergunta ao Felipe. |
| Caveat | Já está restrita a nota e comanda (`--f-manuscrita`). Sem mudança. |
| "∴" no nome | Faz parte do nome que o cliente usa no WhatsApp. Sem mudança. |
| Aba inicial | A banca pede abrir em "Precisa de você". Isso reverte o #1442 (homologação de 07/10), que abria em "Todas". Vale a banca, que é mais nova; a prova do #1442 muda junto. |

## Achados

Estado: **C** corrige nesta entrega, **J** já atende (com evidência), **F** fica de fora (com motivo).

### Tela 1, Lista

| # | Sev. | Achado | Estado | Correção |
|---|---|---|---|---|
| L1 | média | "Aberta" sem contexto; fecha sem confirmar | C | Texto "Loja aberta" com ponto; fechar pede confirmação |
| L2 | média | Lembretes, Avisos e Cozinha só ícone; dois estilos de badge | C | Um estilo de contador numérico; nome acessível com o número |
| L3 | ALTA | "Auto" sem estado | C | "Auto" com o estado escrito ("ligadas"/"desligadas") |
| L4 | baixa | "Abrir assistente" com foto de pessoa | C | Ícone de assistente com rótulo |
| L5 | ALTA | Abas sem contraste; abre em "Todas" | C | Abre em "Precisa de você"; segmento ativo de alto contraste |
| L6 | média | Faixa "N não entregues" grande | C | Faixa compacta com "Revisar" e dispensável até chegar falha nova |
| L7 | média | Avatar com "!" e iniciais erradas ("5") | C | Sem "!" no avatar (o trilho lateral já marca); iniciais só de letras; cor por contato |
| L8 | média | Hora como "9 d", "23 h 50" em cor de marca | C | "21:48" hoje, "ontem", "26 out"; cor neutra; alerta só com prazo estourado |
| L9 | ALTA | Faixa amarela "Automático não soube responder…" | C | Chip curto do motivo ("Restrição alimentar"); frase inteira na conversa e na Ficha |
| L10 | média | Telefone cru "555434619500" | C | Máscara "+55 (54) 3461-9500" |
| L11 | ALTA | Contadores 91/91/11/35 | C | Um contador por sentido; a aba de baixo mostra só o que pede ação |

### Tela 2, Conversa

| # | Sev. | Achado | Estado | Correção |
|---|---|---|---|---|
| A2 | média | "Passou para você" com quadrado de 8 px | C | Faixa de transferência "O automático passou para você · motivo" |
| A3 | média | Várias ações com o mesmo peso | C | Uma ação primária, o resto no menu ⋮ |
| A4 | baixa | Dois menus na conversa | C | Um menu só |
| A5 | ALTA | Conversa abre no topo | C | Abre na última mensagem; botão "Novas mensagens" quando há fala nova fora da vista |
| A6 | média | Balão enviado em largura cheia | C | Máximo de 75 %, cor própria de saída, ponta indicando a direção |
| A7 | baixa | Microfone como botão principal | J/C | Conferir que vira "Enviar" ao digitar |
| A8 | média | Modo nota pouco distinto | C | Fundo de nota e aviso "Só a equipe vê" |
| B1 | baixa | Mensagem de sistema igual a balão | C | Pílula central neutra |
| B2 | ALTA | URL crua da comanda | C | Botão "Abrir comanda N" |
| B3 | baixa | Data some ao rolar | C | Pílula de data fixa no topo ao rolar |
| C1 | média | Resumo do pedido em texto corrido | C | Cartão com números tabulares à direita |
| C2 | ALTA | Link cru do Mercado Pago | C | Link com rótulo "Pagar R$ 19,00" |
| C3 | baixa | Tique só por cor | C | Ícone mais palavra ("entregue", "lida") |

### Tela 3, Ficha

| # | Sev. | Achado | Estado | Correção |
|---|---|---|---|---|
| F1 | baixa | Baixar e editar só ícone | C | Rótulo visível ou menu "Ações da ficha" |
| F2 | baixa | Endereço sem ação | C | Ícone de local, "Copiar" e "Abrir no mapa" |
| F3 | baixa | Valor duas vezes | C | Só o destaque; apoio "1 item · sinal de R$ 19,00" |
| F4 | média | Linha Entrega vazia | C | "Entrega · qui 05/11, 14h–17h · bairro" |
| F5 | ALTA | Rolagem aninhada | C | Uma rolagem só com cabeçalho fixo |
| F6 | baixa | Ajuda de "Marcar pago" longa | C | Uma linha e confirmação com o valor |
| F7 | média | ⋮ ao lado de Marcar pago | C | Um menu só no cabeçalho |

### Tela 4, Menu, Início e Módulos

| # | Sev. | Achado | Estado | Correção |
|---|---|---|---|---|
| M1 | baixa | Tema sem estado ativo | C | Opção ativa marcada |
| M2 | média | Navegação escondida no ⋮ | C | Módulos e Entregas como itens de navegação no topo do menu |
| M3 | média | "Hoje · em andamento · na esteira" no menu | C | Sai do menu; o Início mostra "na fila" |
| M4 | média | Menu com rolagem própria | C | Folha inferior no celular |
| H1 | baixa | Duas marcas no canto | C | Uma marca |
| H2 | média | Seletor de tema no topo do Início | C | Sai do topo; fica no menu |
| H3 | baixa | Contador "Avisos (2)" de 6 px | C | Contador unificado |
| H4 | baixa | Busca de módulos sem lupa | C | Ícone de lupa |
| H5 | média | KPI sem linha de base | C | Número em cima, rótulo embaixo |
| H6 | média | "Na gaveta" órfão | C | Grade 2×2; alerta vira estado do cartão com "Fechar caixa" |
| H7 | baixa | Seta ">" em cartão todo clicável | C | Sai a seta |
| H8 | baixa | Cartão sem linha de estado | C | "Tudo em dia" neutro |
| H9 | média | Cor de severidade igual à marca | C | Severidade nas cores de estado, marca só na ação primária |
| H10 | baixa | "Entregas" órfão | C | Ordem por uso; Configurações como link de rodapé |
| H11 | acerto | Ilustrações dos módulos | J | Manter |

### Acessibilidade e tokens

| # | Achado | Estado | Correção |
|---|---|---|---|
| X1 | 4.1.2: nome composto do item da lista e das abas | C | "Thais Regina, 13 sem resposta, endereço pendente, ontem" |
| X2 | 1.4.1: ✓✓ e "!" só por cor | C | Palavra junto (C3) e sem "!" (L7) |
| X3 | Abas "Conversas · Conversa · Ficha" | C | "Conversas · Mensagens · Cliente" |
| X4 | Pares de contraste no limite | C | Travados no `verificar-contraste` |
| T1 | Escala 12·14·16·20·24·32, lista 15–16 px | C | Nome e prévia do cartão a 16/15 px |
| T2 | Grade de 8 pt | J | Tokens `--e-*` já são múltiplos de 4/8 |
| T3 | Marca só na ação primária e seleção | C | Hora, contador e chips saem da cor de marca |
| T4 | Um estilo de badge | C | L2, H3 |

## Ordem

1. Ganhos rápidos: A5; L5+L11; L3+L9; X1.
2. Projetos: B2+C2+C1; F5; tokens (T1, T3).
3. Baixas.

## Aceite

- Cada achado C tem prova em `ferramentas/prova-2232-*.mjs` que falha antes e passa depois.
- `npm run qualidade` e o gate verdes.
- Revisão independente sem achado.
- Produção validada a 558×684 depois do deploy.

## Rollback

Reverter o squash da PR. Nada muda no banco ou na API.
