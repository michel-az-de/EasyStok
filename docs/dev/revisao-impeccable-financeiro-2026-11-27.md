# Revisão Impeccable do Financeiro do Console, rodada 2 (2026-11-27)

Alvo: as 9 telas do módulo Financeiro (`dominio/modulos.js:108-125`): Caixa do dia, Visão geral, Contas a pagar,
Contas a receber, Extrato, Relatórios (+ Pedidos por anúncio), Categorias, Centros de custo, Fechamentos do aparelho.
Base: master `43dffafbc`. Base de comparação: `revisao-impeccable-financeiro-2026-11-25.md` (não cobriu Caixa nem Fechamentos).

Método: dual-agent (A: crítica Nielsen, Opus, sem ver o detector; B: detector + medição de DOM, Sonnet).
Produção pede login (o agente não digita senha), então as telas foram renderizadas no Vite local em modo API com a
API simulada de `ferramentas/prova-financeiro-navegador.mjs` (exit 0, sem erros de console): 1440 claro e 390 escuro,
mais os modais. Nada foi gravado em dado real. Não medido: desempenho, 1440 escuro, 390 claro, DOM dos modais.

## Notas

| Avaliação | Nota | Faixa |
|---|---|---|
| Crítica (Nielsen) | 26/40 | Aceitável |
| Auditoria técnica (sem desempenho) | 13/16 | Boa |

Auditoria: acessibilidade 3 (erro do modal fora da vista; h2 do menu antes do h1; pulo h2 para h4 no Caixa),
tema 4 (`npm run contraste`: 268 pares aprovados nos dois temas), responsivo 3 (tabela dos Relatórios cortada a 390),
integridade 3 (detector 0 achados; laranja cheio repetido e variante `perigo` ignorada). Desempenho: não medido.

## Medido (B)

- Detector `impeccable detect` em `features/gestao/financeiro` e `features/gestao/caixa`: 0 achados.
- Todas as telas: contraste abaixo do mínimo 0, texto < 12 px 0, overflow horizontal 0, borda só à esquerda 0.
- Laranja cheio (`--acao`) visível: Contas a pagar e a receber 3 (chip "Em aberto", "Nova conta", "Pagar" da linha);
  Visão geral 2 ("Pagar", "Receber"); demais telas 1; Fechamentos 0.
- Falsos positivos descartados: botão sem nome em Pagar/Receber é o "Filtrar" dentro de "Mais filtros" fechado
  (`PainelFinanceiro.jsx:189`, tem texto); "Ver lista ›" 59x20 tem `::after` que cobre o bloco (Q18 de 13/11);
  datas 42x48 a 390 passam no mínimo AA de 24 px.

## Entrega única (só front-end, sem API nova nem decisão de produto)

| # | Sev. | Tela | Achado (visto ou medido) | Correção | Local |
|---|---|---|---|---|---|
| 1 | P1 | Todos os modais do Financeiro | Erro da API renderiza depois de "Observações", fora da vista: em `financeiro-erro-preservado.png` a conta foi recusada e nada aparece | Erro no rodapé, junto do "Salvar", ou no topo com rolagem até ele | `ModaisFinanceiras.jsx:24-27` |
| 2 | P2 | Contas a pagar e a receber | 3 laranjas cheios: o chip ativo usa a cor do primário | Chip ativo com fundo tingido, contorno 1px e ✓ | `financeiro.module.css:59-62` |
| 3 | P2 | Visão geral | "Pagar" e "Receber" são 2 primários | Um só primário (vencidas) ou os dois secundários | `PainelFinanceiro.jsx:62` |
| 4 | P2 | Caixa, Contas, Categorias | "Confirmar estorno", "Cancelar a conta" e "Inativar" em laranja comum, embora o `Botao` tenha `perigo` para isso (#1683) | Variante `perigo`; `FormularioFinanceiro` aceita a variante do salvar | `PromptEstorno.jsx:28`, `ModaisFinanceiras.jsx:24` |
| 5 | P2 | Registrar pagamento/recebimento | Forma de pagamento já vem "pix"; botão genérico "Confirmar registro" | Forma sem valor inicial e obrigatória; botão "Pagar R$ 100,00" / "Receber R$ 100,00" | `ModaisFinanceiras.jsx:155,165` |
| 6 | P2 | Caixa (7 dias) | Dia aberto com tudo R$ 0,00 conta como "dia com movimento" | Contar só entrou ou saiu ≠ 0, ou rótulo "dias com caixa aberto" | `AbaCaixaApi.jsx:449`, `dominio/caixa.js:467` |
| 7 | P2 | Caixa (resumo) | "De onde veio" põe 4 itens em grade de 3; "Total do caixa" solto; saída não é "de onde veio" | Grade 2x2, total como linha de soma com divisor 1px, rótulo "Entrou e saiu" | `AbaCaixaApi.jsx:368-376`, `abaCaixa.module.css:135,197` |
| 8 | P2 | Relatórios a 390 | Tabela corta "Recebido" e "Saldo realizado" sem sinal de rolagem | Cartões com `data-rotulo`, como a tabela dos 7 dias | `PainelFinanceiro.jsx:313` |
| 9 | P3 | Extrato x Relatórios | Período padrão diferente (01/11 a hoje x 01/11 a 30/11) | Mesmo padrão ou frase "previsto até o fim do mês" | `PainelFinanceiro.jsx:291,304` |
| 10 | P3 | Relatórios | "Pedidos por anúncio" a 16 px da tabela anterior | Espaço de seção antes do h2 | `PedidosPorAnuncio.jsx` |
| 11 | P3 | Visão geral | Faixa de 5 atalhos repete o menu lateral visível | Só no celular | `PainelFinanceiro.jsx:20-21` |
| 12 | P3 | Contas (resumo lateral) | "Pagar a próxima parcela" e "Parcelas e histórico" parecem texto solto | Secundário com fundo | `PainelFinanceiro.jsx` (resumo) |
| 13 | P3 | Modal Nova conta | "rascunho" é jargão e vem marcado | Rótulo "Guardar sem lançar (fica só em Todas)" | `ModaisFinanceiras.jsx` |
| 14 | P3 | Categorias | "Ambas" não diz nada; confirmação genérica "Confirmar" | "Pagar e receber"; "Inativar categoria" | `ModaisFinanceiras.jsx:195`, `PainelFinanceiro.jsx:343,346` |
| 15 | P3 | Centros de custo | Ninguém explica o campo ao escolher | Dica "Ex.: Cozinha, Entregas" | `ModaisFinanceiras.jsx` |
| 16 | P3 | Fechamentos do aparelho | 7 totais em grade de 6 (1 sobra); "Saldo" sem destaque; "Não conferida" em texto | `minmax(130px,1fr)`, Saldo em destaque, Pílula | `fechamentosLegados.module.css:21` |
| 17 | P3 | Caixa | Vazio "Nenhum lançamento" 16 px recuado; headings pulam h2 para h4 | Tirar recuo; h3 | `AbaCaixaApi.jsx` |
| 18 | P3 | Prova | API simulada devolve "Outubro" para novembro e `aberto:true` em todo dia, escondendo estados reais | Mês do período e situação variada | `ferramentas/prova-financeiro-navegador.mjs:163,169` |

Opcional na mesma entrega (pendente de 25/11, só front): status Ativa/Inativa em Pílula em Categorias e Centros.

## Fora da entrega (precisa de decisão)

- Contagem cega no fechamento: campo antes do esperado (`ModalFecharCaixa.jsx:75-82`). Decisão de conferência.
- "Fechar o caixa" vira primário à noite (`AbaCaixaApi.jsx:575`). Precisa da regra de horário.
- Menu com 9 portas planas; renomear "Fechamentos do aparelho" (`modulos.js:111-124`). Navegação do app todo.
- Aviso "1 fechamento não pôde ser lido" sem próximo passo (`AbaFechamentosLegados.jsx:21`). Decisão de suporte.
- Botão desligado com borda tracejada; faixa "Notificações bloqueadas" cobrindo o pé. São do app todo.
- Feature nova: filtro Entradas/Saídas no Extrato; pagamento em lote; atalhos de teclado.

## Personas (A)

- Dona que fecha o caixa à noite pelo celular: "Fechar o caixa" é texto pequeno e o laranja chama "Lançamento";
  vê o esperado antes de contar; nada diz se o fechamento tem volta.
- Gerente com 40 contas: paga uma parcela por vez e revisa o Pix pré-marcado em cada uma.
- Operador novo: "suprimento e sangria" sem explicação; estorno sem dizer se volta.
