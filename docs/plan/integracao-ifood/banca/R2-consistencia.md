# R2 - Revisao de consistencia (rastreabilidade, contradicoes, UCs faltando, N, itens 1-8)

Escopo: README, 01 a 08 e fontes. Ids D01-D18, N01-N16, P01-P17, RF01-25, RNF01-18, UC (28 ids), L01-L24: todos referenciados existem; ancoras 02#28-divergencias e 04#46-lacunas batem.

| # | Severidade | Arquivo:secao | Achado | Correcao sugerida |
|---|---|---|---|---|
| 1 | Alta | 06 UC17 E.2 x 04 4.1 x base 3.6 | UC17 E.2 manda "concluir" pedido em Preparando, mas a maquina so permite Preparando->Pronto/Cancelado; contradiz UC23 E.1 ("sem forcar"). Se forcado, pula Pronto e nunca baixa estoque (ComEstoqueDescontado) | Definir: CON em estado anterior a Pronto passa por Pronto (baixando estoque) ou fica registrado/alertado; alinhar UC17, UC23, RF12/RF17 |
| 2 | Alta | 06 UC02 E.1 | "Manter pedidos abertos ate concluir" mas o desvinculo "deixa de enviar o merchant" no polling: CON/CAN/HSD dos pedidos abertos nunca chegam. Caso "loja desvinculada com pedido aberto" contraditorio | Desvinculo so apos zerar pedidos abertos (estado "desvinculando" que continua no polling) ou reconciliar via GET /orders/{id}; criar UC proprio |
| 3 | Alta | 05 RF19/RF20-22, UC01, UC30/UC40 x README fase 1 | Fase 1 lista so Authentication, Events, Order, mas RF19 e UC40 (status da loja), UC01 (GET /merchants) e UC30/RF05 (GET /catalogs, itens) usam Merchant e Catalog. RF19 e fase 1 enquanto UC40 esta na secao Cardapio e loja | Declarar leitura de Merchant e Catalog (somente GET) na fase 1 no README, ou mover RF19 e mapeamento para fase 2 |
| 4 | Alta | 06 | Eventos sem tratamento em nenhum RF/UC: CAR (cancelamento solicitado), DAR (cliente mudou endereco), RPS, DRCR, DGAC. DAR e CAR afetam entrega e cancelamento reais | Adicionar RF/UC para CAR e DAR; RPS/DRCR/DGAC: registrar e ignorar, explicito em UC10 |
| 5 | Alta | 05 RF x 06 UC (rastreabilidade) | UC21, UC22 e UC51 nao constam em nenhum RF (RF12 lista UC17/18/23; RF16 so UC24; UC51 aponta "01 1.2"). Tabela UC marca RF que o RF nao marca de volta: UC02->RF19, UC18->RF13/RF17, UC21->RF12, UC22->RF16; RF06/RF07 citam UC24 mas UC24 so cita RF16 | Incluir UC21,UC22 em RF12/RF16, UC51 em RF novo de homologacao; tornar a relacao RF<->UC simetrica |
| 6 | Media | README Indice | Link para 09-banca.md inexistente (existe pasta banca/) | Apontar para banca/ ou criar 09-banca.md |
| 7 | Media | 05 RF24, README fase 3 x 08 D11 | D11 recomendada A (casa monta no Portal; EasyStok so vincula e empurra disponibilidade/preco) contradiz RF24 (criar itens por PUT /items) e README fase 3 "cardapio gerido pelo EasyStok" | Marcar RF24 e UC31 "se D11=B/D02=C"; redefinir fase 3 (so preco) para o caso recomendado |
| 8 | Media | 08 D15 x 05 | D15 recomendada A exige alerta de fila acima de limite; nenhum RF/UC o cobre. L23 e L17 nao tem RF; L15 so aparece de forma implicita em RNF05 | RF de alerta de fila (fase 1) e UC "pausar loja" manual no Portal; citar L15, L17 e L23 em RF/RNF/D |
| 9 | Media | 06 UC10 E.5 | Reconciliacao apos parada >8 h (GET /orders/{id}) existe como excecao mas sem RF, sem criterio de quais pedidos e sem retomada para pedidos perdidos (nem UC de perda de conexao parcial) | RF de reconciliacao e UC "perda de conexao/retomada"; ligar a UC50 |
| 10 | Media | 06 UC11 | Faltam excecoes: evento/pedido de merchantId desconhecido ou nao vinculado (RNF05 cita, nenhum UC); falha permanente ao processar evento ja gravado (poison/fila de erro); enum/evento desconhecido (RNF12 sem UC); pedido TAKEOUT/DINE_IN (UC14-16 so fluxo de entrega) | Acrescentar UCs ou excecoes e RF correspondentes |
| 11 | Media | 06 UC19/UC20 | Disputa parcial (aceite que reduz itens/valor) nao tem efeito definido sobre itens, estoque e valores (E.4 so cobre cancelamento total); OPA depois de Pronto/Despachado (comanda ja na cozinha, estoque ja baixado so em E.1) sem tratamento; divergencia de valor so em UC11 E.5, nao em OPA | Detalhar disputa parcial e OPA pos-Pronto; repetir conferencia de valor em UC20 |
| 12 | Media | 04 4.1 linha confirm/CFM | Aceite ambiguo: "Aguardando ou Preparando" (hipotese) em 04, "registro, nao estado" em 07, UC12 "CFM confirma". Aguardando->Preparando e a unica saida; se aceite nao muda estado, o KDS pode iniciar preparo sem aceite | Fixar em um texto: aceite = registro; iniciar preparo exige aceito (ou aceita automatico) e dizer isso em UC14 |
| 13 | Media | 08 8.4 | Lacunas das fontes que nao chegaram as duvidas: pagina de erros do Order, changelog/versao da doc, registro e IPs do webhook (afeta D04/P10), retencao 8 h x webhook, valores de {operation} em /status (UC40), tamanho maximo de lote (RF23), multisetup no v2, guia modifiers-management e janelas tecnicas | Acrescentar N17..N24 correspondentes |
| 14 | Media | 06 UC16, UC15 | Despachar sem passar por Pronto: UC16 E.2 envia readyToPickup, mas a maquina permite SaiuParaEntrega apenas a partir de Pronto; baixa de estoque nao e citada no caso | Remover E.2 ou explicitar que a maquina bloqueia, e o KDS exige Pronto |
| 15 | Media | 07 7.1, 05 RNF02 | Contingencia para EasyStok fora do ar (usar Gestor de Pedidos) citada so como risco; sem UC nem RF de modo degradado/retorno e sem conciliacao dos pedidos aceitos fora | UC de contingencia (liga com #9) |
| 16 | Baixa | 06 UC12 E.1 | Timeout do aceite: CAN com `TIMEOUT_EVENT` nao verificado no dicionario nem cancellationCode da fonte | Confirmar no 03 ou marcar [NAO CONFIRMADO] |
| 17 | Baixa | 05 RF25 | Fase "3 (se D12=B)" mistura fase e condicao; RF20 depende de D12=A | Coluna Fase simples e coluna Condicao |
| 18 | Baixa | 08 | Ordem D01,D02,D14,D15,D18,D05... fora de sequencia numerica | Ordenar por id ou indice no topo |
| 19 | Baixa | 06 UC41 x D15 | "Pausar loja" e fase 2 (RF21) mas README/D15 A depende de pausa manual na fase 1 | Dizer onde se pausa na fase 1 (Portal do Parceiro) |
| 20 | Baixa | 06 UC03 | Token compartilhado por varias lojas: expiracao no meio do ciclo/401 em cascata nao tratada | E.3: refazer token e repetir a chamada uma vez |

## Itens 1-8 da missao
- 1 Preparacao (01): completo, 17 itens com quem/bloqueio/fonte.
- 2 API (02): completo; faltam N de #13.
- 3 Dicionario (03): ok; lista enums, mas sem CAR/DAR/RPS tratados (#4).
- 4 Mapeamento (04): bom; estado do aceite ambiguo (#12); L15, L17, L23 sem RF (#8).
- 5 Requisitos (05): rastreio assimetrico (#5); faltam RFs de reconciliacao, alerta de fila, homologacao, eventos CAR/DAR.
- 6 Casos de uso (06): excecoes faltantes em #2, #4, #9-#11, #14-#15. Cobertos: pedido duplicado (UC11 E.6), pedido de teste (E.3), CAN apos entregue (UC18 E.1).
- 7 Analise (07): dois olhares presentes; sem dimensionamento (declarado).
- 8 Duvidas (08): multipla escolha com recomendada; D11/D15 em conflito com RF (#7, #8).
