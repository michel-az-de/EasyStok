# Revisão Impeccable do Financeiro do Console (2026-11-25)

Alvo: `EasyStock.Console/src/features/gestao/financeiro` (Visão geral, Contas a pagar e a receber, Extrato,
Relatórios + Pedidos por anúncio, Categorias, Centros de custo e modais). Base: master `b27ec7807`, issue #3331.

**Contexto da leitura do pedido.** Não havia revisão Impeccable do Financeiro (só a do Atendimento,
`revisao-impeccable-atendimento-2026-11-24.md`). A revisão foi feita aqui, antes de aplicar. Os dois achados
antigos do Financeiro em `revisoes/2026-11-13-qa-console-web.md` já estavam resolvidos: Q09 (data na legenda
virou por extenso) e Q18 (refutado: o `::after` do "Ver lista" cobre o bloco todo).

Método: detector (`impeccable detect`: 0 achados antes e depois), código lido de ponta a ponta, 19 provas
do Financeiro rodadas, capturas 1440 claro e 390 escuro (`builds/antes` e `builds/depois`), computed style
medido na prova de navegador. Atendimento e Notificações bloqueadas ficam fora.

## Achados e destino

| # | Sev. | Achado (medido) | Correção | Prova |
|---|---|---|---|---|
| 1 | P2 | Visão geral: "A pagar"/"A receber" (h2) com 17 px e o bloco "Próximos 7 dias" (h3) com 15 px: só 2 px de diferença, o lado quase não manda no bloco | `.ladoDia h2` passa a `--t-titulo` (20 px) | `prova-financeiro-navegador`: h2 ≥ h3 + 4 px |
| 2 | P2 | Contas: 3 laranjas cheios à vista com a conta escolhida ("Nova conta", "Pagar" da linha e "Pagar a próxima parcela" do resumo, que repete a linha) | Resumo vira secundário; a linha fica com o primário | idem: "Pagar a próxima parcela" não é primário e continua na tela |
| 3 | P2 | Relatórios: o fluxo de caixa não tinha título visível (só "Pedidos por anúncio" tinha) e havia 2 laranjas ("Gerar relatório" e "Consultar anúncios") | h2 "Fluxo de caixa"; "Consultar anúncios" secundário; h2 sem margem extra | idem: 1 heading "Fluxo de caixa", único primário "Gerar relatório" |
| 4 | P2 | Extrato: "Resultado dos movimentos" negativo sem cor, enquanto a saída na lista é vermelha | `negativoEmErro` no `Indicador` usa a cor de saída (sinal "−" e texto continuam) | idem: cor do resultado = cor da saída |
| 5 | P3 | Visão geral: frase "Nada a pagar nos próximos 7 dias" com 14 px de margem de parágrafo a mais (lacuna 22 px) | `.blocoDia > .apoio { margin: 0 }` | idem: margem 0 |
| 6 | P3 | Modal Nova conta: explicação de "Lançar a conta ao salvar" a 16 px da caixa, solta no meio do formulário | `.escolha + .aviso` com margem negativa (12 px mais perto) | captura `financeiro-modal-nova-conta.png` |

## P3 que ficam de fora, com motivo

- Categorias e Centros mostram "Ativa/Inativa" em texto simples, e as contas usam Pílula. Trocar é decisão de
  padrão visual dos cadastros (não medida aqui): fica para PR própria.
- Cartão "Vencidas R$ 0,00" ocupa a altura inteira mesmo sem nada vencido: quieter é decisão de produto
  (o cartão é o atalho para a lista de vencidas).
- Faixa "Notificações bloqueadas" cobre o pé a 390 px: é do app todo, não só do Financeiro: fora do escopo desta PR.

## Conflitos (D04)

O worktree `onda3-1769` tem diff não commitado em `PainelFinanceiro.jsx`, `ModaisFinanceiras.jsx` e
`dominio/financeiro.js` (46 arquivos, parado há 6 semanas). Esta PR não toca `ModaisFinanceiras.jsx` nem
`dominio/financeiro.js`; em `PainelFinanceiro.jsx` mexe em 4 linhas.

## Notas

Não medidas: não houve rodada independente de crítica (Nielsen) nem de auditoria pontuada. O que está medido é
o detector (0 achados), o computed style e as provas acima.
