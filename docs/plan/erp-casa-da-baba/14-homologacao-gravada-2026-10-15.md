# Homologação gravada de 15/10/2026: spec da rodada

Issue: #1839 · Base medida: produção e master em `85484cf0` · Gravação: 6m34s, Balcão, Cardápio e tela inicial.

## Causas medidas

| # | Causa | Evidência |
|---|---|---|
| 1 | Reabrir não solta o pedido antigo | `Conversa.Reabrir` (Domain `Conversa.cs`) não mexe em `PedidoEmAndamentoId`; `ObterPedidoConversaUseCase` devolve esse pedido mesmo entregue |
| 2 | Não há como gerar outro link de pagamento | Console `aplicacao/api/comanda.js` acha o pedido já criado; `dominio/resumoPedido.js` trava a cobrança com a janela fechada (#1816) |
| 3 | Imprimir pede três cliques | `componentes/ImprimirPedido.jsx`: Preparar prévia, Abrir impressão, Sim, o papel saiu; a prévia vazia é o estado inicial |
| 4 | Impressão em dois caminhos | Canhoto, KDS e fila ainda chamam o PDF de `dominio/impressao.js`; o Encerrar usa `window.print()` da folha React |
| 5 | Animação de pago repete | `RelogioPix.jsx` remonta dentro da modal Pagamento e toca de novo a cada abertura |

## Decisões do Felipe (15/10)

- Impressão pelo diálogo do navegador, com prévia real. QZ Tray fica fora.
- A última nota interna fica fixa como post-it no Resumo. Fixar uma nota escolhida exigiria migração.
- Cards do hall com ilustração nova por módulo, aprovada antes de entrar.
- Entregas: cobrança na porta; janelas, bloqueios e zonas; atalho Automáticas fora do Balcão; despacho automático (#1597) em rodada própria.

## Pacotes

Nenhum pacote tem migração. Cada pacote é uma PR.

### B. Impressão única

| Item | Mudança | Aceite |
|---|---|---|
| B1 | `ImprimirPedido` prepara a prévia ao abrir; um botão "Imprimir"; confirmação do papel fica discreta | Abrir o modal mostra o layout em até 1 clique |
| B2 | Canhoto abre no documento de cozinha e mostra a prévia da API no lugar do papel React | "Canhoto 5" mostra o layout bilhete |
| B3 | Botões de canhoto do KDS, da comanda do KDS e da fila passam pelo mesmo fluxo | Nenhum chamador do PDF próprio para canhoto |
| B4 | Modelo `resumo` do atendimento na API, no estilo do bilhete; o "Exportar PDF" do Encerrar usa a API | Resumo sai em A4 e A5 sem estouro |
| B5 | Links "Consultar Lalamove" e "Abrir Entregas" saem do pedido do cliente | O despacho fica em Entregas |
| B6 | Se o `print()` não abrir sob dois `<dialog>`, o iframe vai para dentro do diálogo aberto | Diálogo do sistema abre no laboratório |

### A. Balcão: ciclo e painel

| Item | Mudança | Aceite |
|---|---|---|
| A1 | Reabrir solta o pedido finalizado; o Resumo ignora pedido entregue ou cancelado | Conversa reaberta começa sem pedido; o antigo segue em Cliente e histórico |
| A2 | Sem pedido, o Resumo mostra o bloco Comanda do protótipo: cardápio, link de compra e link de pagamento | Atendimento novo cria pedido novo |
| A3 | Ficha: "N pedidos no total", total gasto e prato favorito vindos do dossiê; resumo do cliente por IA sobre `IAgenteLlmClient` e `ResumoDossieParaAgente` | Números batem com a API; resumo com fallback quando a IA falha |
| A4 | Nota: caixinha pequena no composer; a última nota fica como post-it no topo do Resumo, com tags e cadastro | Sem modal; nota visível sem abrir nada |
| A5 | Composer como o protótipo: "Modelos aprovados" dentro de Respostas quando a janela fecha | Um ponto de entrada para mensagens prontas |
| A6 | Link de pagamento gerado com a janela fechada, para copiar ou mandar por modelo | Gera sem enviar |
| A7 | Animação de pago só na chegada do pagamento | Reabrir a modal não anima |
| A8 | Extrato do pedido: leitura que junta pagamentos, sinal, frete e estornos | Extrato na modal Pagamento |

### C. Link de compra (site `casa-da-baba` + API)

Finalizar sem endereço pelo caminho "entrega a combinar" (não cria pedido nem cobrança); aviso que não bloqueia; tela final "Obrigado, já avisamos a Casa; dúvida, fale com a atendente"; raios pelos tokens do site.

### D. Tela inicial

Arrastar direto os contadores e os cards (dnd-kit já instalado), ordem dos contadores em `preferenciasHall`; card com ilustração e pendência viva nos módulos com dado.

### E. Cardápio

Duplo clique na linha abre o editar; "Escolher foto" do banco de imagens, reaproveitando `POST /api/acervo/imagens/{id}/publicar`.

### F. Entregas

F1: Pago só com saldo zero e "Entregue" avisa com saldo a receber (#1776 item 19); janelas, bloqueios e zonas com validação, editar e excluir (#1776 itens 20 a 22); atalho Automáticas no hall e no menu. F2: despacho automático conforme #1597, em rodada própria.

## Método

1. Laboratório no modo API com o mesmo pedido no master e na branch, em 1280, 1440 e 1920.
2. Banca visual de 3 revisores sobre as capturas; cada achado confrontado no código.
3. Validação final na tela publicada.
