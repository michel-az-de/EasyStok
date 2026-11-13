# Banca do financeiro: contas a pagar e a receber (27/10/2026, #2095)

Base `origin/master` 04437f8ed. Laboratório isolado (API 18916, banco 55916, console 5791 com o financeiro descongelado).
Cinco revisores independentes: R1 pesquisa de UX em ERPs e comunidade, R2 domínio e use cases com testes de prova,
R3 console e contrato com a API, R4 API real por HTTP com conferência no banco, R5 tela real medida pelo DOM em
1280/1440/1920/390. Custo medido dos revisores: cerca de 1,07 milhão de tokens (127k, 289k, 227k, 245k, 179k).

Decisão do Felipe nesta rodada: **descongelar o financeiro todo** (revoga para essas telas a #1891).

## Veredito

O núcleo funciona: criar, parcelar em centavos, emitir, baixar, estornar e lançar no Caixa; concorrência na mesma
parcela é barrada pelo `xmin`; estorno é idempotente. Mas a tela **não responde às perguntas do dia** ("o que vence hoje
e nesta semana?", "quem me deve?") e a API tem **3 caminhos que tiram dinheiro do razão**.

## Achados confirmados (consolidados)

Ids originais entre parênteses. Gravidade: P0 dinheiro errado ou perdido; P1 fluxo quebrado, erro 500/400 indevido ou
regra violada; P2 inconsistência ou atrito; P3 cosmético.

### API e domínio

| Id | Grav. | Fato | Onde |
|---|---|---|---|
| F1 | P0 | Pagamento aceito em conta **Rascunho**; "Remover parcela" depois apaga o pagamento em cascata e o movimento do Caixa fica sem estorno (R4-1, R4-2, R2-11) | `ParcelaPagar.cs:70`, `ParcelaReceber.cs:75`, `ContaPagar.cs:122`, `ContaReceber.cs:123` |
| F2 | P0 | Conta cancelada com parcela paga: o estorno volta a parcela para Pendente e ela aceita **novo pagamento** em conta Cancelada; o job de vencimento a notifica (R4-3, R2-12) | `EstornarPagamentoParcelaUseCase.cs:55`, use cases de registro |
| F3 | P0 | `POST /contas-a-receber/parcelas/{id}/reconciliar` ignora a parcela da rota e a empresa, busca por txid com `IgnoreQueryFilters` e confia no valor do corpo; o excedente some por `Math.Min` (R4-4, R2-10) | `ContasAReceberController.cs:211`, `ContaReceberRepository.cs:165` |
| F4 | P1 | Filtro "Vencimento de/até" responde 400 com texto do Npgsql ("Kind=Unspecified"); o console manda data pura (R5-1, R4-6, R3-1, R2-2; #1776 item 10) | `ContasA*Controller.cs`, `Conta*Repository.cs:119` |
| F5 | P1 | Registrar pagamento exige só **Visualizar**; perfil de consulta tira dinheiro do Caixa (R2-1, R4-12, R3-11; #476) | `ContasAPagarController.cs:150`, `ContasAReceberController.cs:153` |
| F6 | P1 | `documentoReferencia` repetido (NF 123 de outro fornecedor, ou conta manual) devolve **201 com a conta antiga** e a nova dívida não é registrada (R4-10, R2-4) | `CriarContaPagarUseCase.cs:55`; índice único `(EmpresaId, DocumentoReferencia)` |
| F7 | P1 | Texto longo, `valor` 1e15, método com 21 letras, FK inexistente e página enorme viram **500** (R4-13, R2-5; #1776 item 12) | use cases sem validação de tamanho |
| F8 | P1 | `valor: 0.004` passa (valida antes de arredondar) e grava pagamento de R$ 0,00; método é texto livre ("bitcoin") (R4-11) | `RegistrarPagamento*UseCase.cs:37` |
| F9 | P1 | `dataPagamento` só com a data vira 00:00Z (21h do dia anterior em Brasília); data futura aceita (R4-9) | `DataUtc.ParaUtcOpcional` nos registros |
| F10 | P1 | CR gerada do pedido usa `pedido.Total` e ignora o que já foi pago: pedido pago no site vira conta aberta e depois "vencida" (R2-3, opt-in por loja, desligado hoje) | `GerarContaReceberDePedidoUseCase.cs:64` |
| F11 | P2 | Ordenação "datavencimento" não existe e cai na data de emissão: a conta que vence amanhã fica no fim (R2-13, R3-4, R5-3) | `Conta*Repository.cs:137-142` |
| F12 | P2 | Filtro de status usa `DateTime.UtcNow.Date`; entre 21h e 24h a lista e a pílula discordam (R2-14, R4-18; #667) | `Conta*Repository.cs:94` |
| F13 | P2 | Fluxo de caixa agrupa por meia-noite UTC, gera linha extra no fim e corta em 24 linhas sem aviso (R4-16, R4-17, R3-2) | `FluxoCaixaQueries.cs:194-230` |
| F14 | P2 | Pagamento em dinheiro sem caixa aberto aceito e deixa a gaveta negativa; movimento sempre no dia de hoje, mesmo com data retroativa (R4-8, R4-9, R3-9) | ligado às decisões do caixa da #2084 |
| F15 | P2 | Categoria com `tipo: 99` aceita; centro aceita `<script>`; `busca=%` traz tudo; emitir duas vezes grava dois eventos (R4-15, R4-24, R4-25, R4-23) | use cases de cadastro e listagem |
| F16 | P1 latente | Pix por Efí: estornado e ainda CONCLUIDA na Efí quebra o job a cada hora; Pix pago depois do vencimento nunca concilia; cancelar a conta não limpa o QR; txid fixo por parcela (R2-6 a R2-9, R4-19) | `ReconciliarPix*`, `GerarPixQr*`, job. Efí não está configurada |
| F17 | P2 | Operador recebe 403 em tudo, mas `/api/auth/me/modulos` libera o Financeiro (R4-21) | mapa de módulos |

### Console

| Id | Grav. | Fato | Onde |
|---|---|---|---|
| C1 | P1 | Visão geral sem "vence hoje" nem "esta semana"; ignora `qtdParcelasVencidasHoje`; cartões não levam à lista filtrada (R5-7) | `PainelFinanceiro.jsx:30-43` |
| C2 | P1 | Pílula "Vencida" com `tom='erro'`, que não existe: vencido sem destaque (R5-2) | `PainelFinanceiro.jsx:28` |
| C3 | P1 | Conta cancelada mostra "Total R$ 0,00", parcelas com saldo e "Vence dd/mm" (R5-5, R5-6) | `PainelFinanceiro.jsx:20,110,113` |
| C4 | P1 | Relatório padrão termina hoje: o "a pagar" dos próximos dias some (R5-8) | `dominio/financeiro.js:11` |
| C5 | P2 | "Editar conta" aparece em conta emitida e a API recusa ("So e possivel atualizar conta em Rascunho") (R3-3) | `PainelFinanceiro.jsx:106` |
| C6 | P2 | Rascunho e cancelada misturados no filtro padrão; valor em negrito sem rótulo (é o pendente) (R5-10, R5-11) | `PainelFinanceiro.jsx:50,75` |
| C7 | P2 | Pagar exige 3 cliques e 2 modais empilhados (Abrir, Registrar, Confirmar); conta recém-criada não abre (R5, R3-4) | lista e detalhe |
| C8 | P2 | Salvar desabilitado sem motivo; 1º vencimento antes da emissão aceito (o Razor validava) (R3-6, R3-7) | `ModaisFinanceiras.jsx` |
| C9 | P2 | 390 px: 1 linha acima da dobra; resumo da conta selecionada em y=4138 (R5-15, R3-17) | `financeiro.module.css` |
| C10 | P3 | Textos de "pagamento" em contas a receber; status cru "Confirmado"; mensagens da API sem acento (R5-17, R3-16, R5-18) | vários |
| C11 | P3 | Sem "Reativar" categoria e centro (a API tem) (R3-13) | `PainelFinanceiro.jsx:191` |
| C12 | P3 | Página vazia sem saída depois de pagar com filtro ligado (R3-8) | paginação |

## O que a pesquisa de UX recomenda (R1)

Para uma dona de restaurante pequeno, os produtos que funcionam resolvem: (1) uma tela "Hoje" com Atrasadas, Vencem
hoje e Próximos 7 dias, com total em R$ e ordem por vencimento; (2) lançar em poucos campos; (3) "Paguei" num toque
com data de hoje e valor cheio, expandindo só para parcial; (4) estorno que reabre e mantém o histórico; (5) recorrência
mensal; (6) a receber ligado ao pedido e ao Pix; (7) aviso diário no sino. O erro mais reclamado no mercado é
**duplicidade e baixa fantasma** em fluxos automáticos: tudo idempotente e reversível (F1, F2, F3, F16 são deste tipo).
Não vale para a Casa da Baba: rateio de centro de custo multinível, aprovação em vários níveis, conciliação bancária
automática, CNAB/DDA, retenções.

Fontes principais: ajuda Omie (cadastrando contas a pagar), Conta Azul (baixa parcial, juros e multa, filtros, competência
x vencimento), Bling (alertas de vencimento), QuickBooks (bill pay, void x delete), Xero (cash flow), Asaas (régua de
cobrança), Baymard (filtros aplicados), NN/g (formulários e tabelas no celular), Reclame Aqui e Capterra (duplicidade).

## Lacunas funcionais (não entram nesta rodada; viram issues)

Fornecedor e cliente na conta e "quem me deve" (o domínio tem `FornecedorId`/`ClienteId`); recorrência mensal; juros,
multa e desconto na baixa; renegociar parcela de conta emitida; baixa em lote; anexo; Pix da parcela no console;
intervalo quinzenal/semanal; saldo projetado 7/30 dias; índice de `DocumentoReferencia` por fornecedor (migração).

## O que está bem e não se mexe

Concorrência (200 + 409), pagar acima do saldo (400), estorno idempotente e com motivo, arredondamento em centavos,
bloqueio de edição em conta emitida, categorias sem mistura de tipo e sem nome duplicado, paginação normalizada,
dashboard com o dia de Brasília, CSV com BOM, `;` e vírgula decimal, nenhum overflow horizontal da página.
