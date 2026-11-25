# Medição em modo API: teclado no Balcão (#3358)

Base: `master` d93dfbfbc, 25/11/2026. Console no modo API (`VITE_FONTE_DADOS=api`), Chromium, 1440x900, `reducedMotion`. Rede simulada com `page.route('**/api/**')` e sessão de teste no `sessionStorage`, o mesmo arranjo da `prova-2395-memoria-atendimento-navegador.mjs`. Nenhuma chamada saiu para produção. O script de medição não entra no repositório: a primeira ação da PR 5 o transforma em `ferramentas/prova-<issue>-fila-teclado.mjs` (S1).

## O que foi medido

| Medida | Resultado | Leitura |
|---|---|---|
| Ctrl+K sem conversa aberta | Abre a Busca na Casa: um `combobox` "Nome, telefone, e-mail, ID ou tela" recebe o foco | **Existe no modo API.** O plano dizia "só no ramo API, sem medida"; agora tem. Busca pessoas, conversas, pedidos, produtos e telas (`BuscaNaCasa.jsx`); a rede simulada devolveu lista vazia, então a abertura de uma conversa por Ctrl+K não foi medida |
| Tab do início do documento até o campo da mensagem (aba "Todas", 35 cartões, conversa aberta) | **56 paradas**; cada cartão é uma parada | Na demonstração, aba "Precisa de você" com 11 cartões, o plano mediu 35. A diferença é o número de cartões, não o modo |
| Foco ao abrir a conversa por clique | No campo "Escreva para Pessoa 05" | Já bom, não mexer |
| Shift+Tab do campo até o cartão da conversa aberta | **11 paradas** | Passa pela barra do modo, pelo fio e pelos botões |
| Do cartão aberto ao cartão seguinte | 1 Tab | O cartão seguinte vira a parada seguinte |
| Enter no cartão | Abre a conversa e o foco cai no campo | Já bom |
| T13 (do campo à próxima conversa, só teclado) | **13 teclas** (11 Shift+Tab, 1 Tab, 1 Enter) | O plano mediu 16 na demonstração |
| ↓ e `j` com o foco num cartão | **O foco não anda** | Confirma F01 em modo API |
| Esc no campo com texto | O foco e o texto ficam onde estavam | Confirma F04 |
| Ctrl+Enter no campo com texto | **0 envios**; o texto fica | Confirma que o campo ignora Ctrl+Enter (`Composer.jsx`, `onKeyDown` sai se `ctrlKey`); sem conflito com a decisão 1 |
| Shift+Tab do campo até "Buscar conversa" | **43 paradas** | O plano mediu 19 na demonstração; o número cresce com os cartões |

Erros de página durante a medição: 0. Chamadas que não são GET: só `marcar-lida`.

## O que esta medição não cobre

- A fila "Precisa de você" ficou vazia: o cenário simulado marca as conversas como "Com você" (assumidas por quem atende) e não gera motivo de espera. T1 e T13 foram medidos na aba "Todas". A S3 e a S4 medem a fila "Precisa de você" com motivo, na demonstração e no modo API, antes de mexer no código.
- Ctrl+Enter com o Assistente atendendo (modal "Enviar e assumir?") não foi exercitado em modo API.
- Servidor real, SSE e duas abas ficam fora (F25, F28).
- Leitor de tela, Firefox e Safari não foram usados.
