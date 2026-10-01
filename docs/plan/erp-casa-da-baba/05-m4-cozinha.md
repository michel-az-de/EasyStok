# M4 Cozinha · fila de preparo, prioridade, itens, tempo de preparo e expedição

Issue: #1316 · Decisão: [ADR-0056](../../adr/0056-erp-da-casa-da-baba-front-unico.md) · Data: 2026-10-01
Base medida: master `b713263a`. Plano irmão (dono de S01–S53 e F01–F18):
[11-console-fechamento.md](../atendimento-whatsapp/11-console-fechamento.md), [12-impressos.md](../atendimento-whatsapp/12-impressos.md).
Fundação: [01-fundacao.md](01-fundacao.md) (M0.2 rota `#/m/cozinha/fila`, apelido `#/cozinha`; M0.3 perfil
Cozinha com porta "M4 fila"; D-05 sessão longa do tablet). Este documento não reescreve S nem F.

## 1. Veredito

**A FILA JÁ FUNCIONA NA API (S19 + F05). O QUE FALTA É O QUE A DONA USA COM A MÃO NA MASSA: SOM, ORDEM POR PRAZO, O QUE PRODUZIR AGORA E O TEMPO REAL.**

| Medida | Valor | Fonte |
|---|---|---|
| Fatias mergeadas que formam a cozinha | S15, S18, S19, S20, S21, S46, S49, S52; F05, F08 | `gh pr list` (#1133, #1150, #1162, #1167, #1170, #1188, #1270, #1282, #1220, #1266) |
| PR aberta | S53 número do dia (#1293) | `gh pr list` |
| Sem PR | F11 lote de papel (#1241), F12 paridade visual (#1242), S50 "pronto até", S51 consumidores de impressão | `gh issue view`; `12-impressos.md:88-118` |
| Ordem da fila no backend | **por criação**, não por janela | `EasyStock.Infra.Postgre/Repositories/KdsPedidoQueries.cs:41` |
| Estado por item | não existe: item do KDS é só nome, variação, quantidade, observação, linha, molho | `KdsPedidoQueries.cs:97-106` |
| Tempo real de preparo | não é lido, mas **já é gravado**: cada troca de status grava `PedidoEvento` com hora e usuário | `AtualizarStatusPedidoUseCase.cs:108-119` |
| Som na cozinha do modo API | **não tem**: `useAvisoSonoro` é usado pela cozinha da demonstração, não pela da API | `features/cozinha/TelaCozinha.jsx` usa; `TelaCozinhaApi.jsx` e `aplicacao/useCozinhaApi.js` não |
| Fatias novas deste documento | **7** (M4.1 a M4.7) | §4 |

## 2. Como o M4 vive dentro do shell

| Regra | Fato que apoia |
|---|---|
| Tela de dispositivo: o perfil Cozinha cai direto em `#/m/cozinha/fila`, sem sala, sem menu lateral | ADR-0056 item 4; matriz da M0.3 em `01-fundacao.md` |
| `#/cozinha` continua abrindo a mesma tela (apelido) e a janela do balcão continua abrindo por `window.open` | `01-fundacao.md` M0.2; `app/Moldura.jsx:209-218` |
| Tablet de parede não pode cair no fim do turno: sessão longa só para perfil de dispositivo | D-05 em `01-fundacao.md`; hoje o JWT dura 8 h sem refresh (`11-console-fechamento.md:95`, F07 item 6) |
| Operação por som, toque e papel (D6): alvo ≥ 48 px, um toque por passo, rótulo junto da cor | RN-30, RN-31 (`Downloads/03-ANALISE…md:176-186`) |
| Dona com dois perfis (atende e cozinha) usa a Dona e abre a cozinha em outra janela, como hoje | persona "cozinha, atende, embala e vende sozinha" (`Downloads/02-ESTORIAS…md:34`) |

```
 tablet (perfil Cozinha)                    balcão (perfil Atendimento ou Dona)
 #/m/cozinha/fila                            #/m/atendimento ──► botão Cozinha ↗ (window.open)
 ┌──────────────────────────────────────────────────────────────────────┐
 │ Hoje ▾ │ Linha: todas ▾ │ ao vivo ●                      Agora: 6 itens │
 ├────────────┬────────────┬────────────┬────────────┬──────────────────┤
 │ Pago       │ Em preparo │ Embalado   │ Em entrega │ Entregue (recolh)│
 │ ordem: prazo (M4.3)  ·  tempo real no cartão (M4.4)                    │
 └──────────────────────────────────────────────────────────────────────┘
```

## 3. Mapa: sub-tela → fatia que cobre → estado

Legenda: ✅ mergeada · 🟡 PR aberta · ⬜ sem PR · ❌ sem fatia (vira M4.x).

### 3.1 Fila de preparo

| Peça | Backend | Console | Estado | Lacuna |
|---|---|---|---|---|
| Fila por status, um toque por passo | S19 (`KdsController.cs:27,41`) | F05 #1220 | ✅ | nenhuma |
| Ao vivo (SSE) e polling de reserva | S18 | F05 (`useCozinhaApi.js`: 15 s sem SSE, reconecta a cada 10 s) | ✅ | nenhuma |
| Dia de produção: vaga ativa, senão agendamento, senão criação | `KdsPedidoQueries.cs:34-40` | F05 | ✅ | nenhuma |
| Aprovação sem corte de data | F08 #1266 (`KdsPedidoQueries.cs:28,35`) | F04 | ✅ | nenhuma |
| 5 colunas do protótipo, cartão com itens por linha, despacho no cartão | dados já vêm no KDS | F12 #1242 | ⬜ | nenhuma |
| Filtro por linha (US-041) | `GET api/kds/pedidos?linha=` (`KdsController.cs:30`) | F12 | ⬜ | nenhuma |
| Som de pedido pago e de atraso (D6, RN-25, UC-04 passo 1) | SSE `pedido.*` (S18) | não existe no modo API | ❌ | **M4.2** |
| Teto de 200 cartões por consulta | `KdsPedidoQueries.cs:14` | nenhuma | ✅ | ver M4.6 com vários dias |

### 3.2 Prioridades

| Peça | Fato | Estado | Lacuna |
|---|---|---|---|
| Início previsto e atraso | `Pedido.InicioPrevistoEm` (`Pedido.cs:183`), S21 #1170 e #1250 | ✅ | nenhuma |
| "Pronto até" por loja e por pedido | S50 especificada (`12-impressos.md:88-104`); `ProntoAteEm` não existe no domínio (busca vazia) | ⬜ | nenhuma |
| Ordem da fila | `OrderBy(p => p.CriadoEm)` (`KdsPedidoQueries.cs:41`); a tela não reordena (`TelaCozinhaApi.jsx:39-40` só filtra por coluna) | ❌ | **M4.3** |
| Prioridade marcada pela dona | não existe | ❌ | D4-01 |

Fato da entrevista: a dona prende o canhoto "no quadro de cortiça, por horário de entrega"
(`Downloads/03-ANALISE…md:314`). **Inferência:** a ordem que ela usa no papel é o prazo, não a chegada.

### 3.3 Itens em produção

| Peça | Fato | Estado | Lacuna |
|---|---|---|---|
| Itens no cartão, com linha, porção, molho e observação (RN-20, RN-29) | `KdsPedidoQueries.cs:97-106` | ✅ dado; visual na F12 | nenhuma |
| Total do que produzir agora, somado entre pedidos | não existe | ❌ | **M4.5** |
| Marcar item feito ou embalado | não existe; a comanda em papel tem caixas e "Feito por / Embalado por" (S52, `12-impressos.md:133-136`) | ❌ | D4-02 |

### 3.4 Tempo de preparo

| Peça | Fato | Estado | Lacuna |
|---|---|---|---|
| Tempo previsto por item | `CardapioItem.TempoPreparoMinutos` (S15 #1133), usado no prazo (`ListarJanelasAtendimentoUseCase.cs:51-60`) | ✅ | nenhuma |
| Tempo real | `PedidoEvento` `status_changed` com `StatusAntigo`, `StatusNovo`, `OcorridoEm`, `UsuarioId` (`AtualizarStatusPedidoUseCase.cs:108-119`; `Pedido.cs:369-387`) | gravado, não lido | **M4.4** |
| Capacidade de janela com linha mista (US-027, Q2) | questão aberta "Tatiana, medindo tempos reais" (`Downloads/03-ANALISE…md:551`; `02-ESTORIAS…md:806`) | ⬜ | M4.4 mede; regra fica em D8-05 (`09-m8-entregas.md`) |

### 3.5 Impressão e papel (plano B)

| Peça | Backend | Console | Estado | Lacuna |
|---|---|---|---|---|
| Canhoto 80 mm e reimpressão | S20 (`ImpressaoController.cs:41,83`) | F05 | ✅ | nenhuma |
| Fila de impressão do bridge (impressão sem clique, RN-27) | S20 (`ImpressaoController.cs:59,71,77`) | fora do console (onda 0.6, `10-console.md` F05 "Fora") | ✅ backend | a medir no bridge |
| Impresso do pedido (10×15, 58 mm, A4) | S49 #1270 (`PedidoImpressoController.cs:23`) | S51 (sem PR) | ⬜ | nenhuma |
| Comanda de cozinha | S52 #1282 (`PedidoImpressoController.cs:40`) | não chamada pelo console (busca por `/comanda` em `features/` vazia) | ❌ | **M4.7** |
| Número do dia na comanda e no cartão | S53 | PR #1293 aberta | 🟡 | M4.7 mostra no cartão |
| Lote de papel (status em lote quando a conexão volta, UC-04 E1) | S46 (`AtendimentoEsteiraController.cs:23`), também `POST api/kds/pedidos/status-lote` (`KdsController.cs:59`) | F11 #1241 (hoje avisa, `naoLigadas.js:54`) | ⬜ | nenhuma |

### 3.6 Expedição

| Peça | Fatia | Estado | Observação |
|---|---|---|---|
| Pronto → saiu para entrega, com entregador (RN-32) | S12, S44; F04 | ✅ | dono: M8 |
| Despacho no cartão (entregador, veículo, placa) | F12 | ⬜ | nenhuma |
| Conferência na retirada pelo número do dia | protótipo (`dominio/despacho.js:90`); S53 | 🟡 | dono: M8.4 |
| Encomendas de amanhã visíveis na cozinha | `GET api/kds/pedidos?data=` existe (`KdsController.cs:31`), o console não manda `data` (`infra/api/kdsApi.js:6`) | ❌ | **M4.6** |

## 4. Fatias novas

Convenções do [README §4](README.md); aceite fixo "sem `VITE_FONTE_DADOS`, o console abre como hoje".

### M4.1 · A cozinha como tela de dispositivo no shell

**Problema.** Hoje a cozinha é aberta pelo balcão (`window.open`) ou pela rota digitada; não existe "o tablet
da cozinha" como entrada própria, e a sessão de 8 h derruba a tela no meio do turno.
**Abordagem.**
- `#/m/cozinha/fila` como porta do perfil Cozinha (M0.3), sem sala e sem menu lateral; topo mínimo com logo (M0.1), "ao vivo" e "sair".
- Sessão do perfil de dispositivo conforme D-05 do `01-fundacao.md` (refresh token, "sair" explícito).
- Tela cheia e tela sempre acesa quando o navegador permitir (Wake Lock); sem suporte, segue normal.
**Aceite.**
- [ ] Usuário do perfil Cozinha faz login e cai na fila, sem ver a sala.
- [ ] Com relógio falso em 9 h de sessão, a fila continua ao vivo (refresh) e não perde o filtro.
- [ ] `#/cozinha` abre a mesma tela de hoje.
- [ ] Perfil Cozinha recebe 403 da API em rota de atendimento.
**Depende de.** M0.2, M0.3, D-05. **Tamanho.** P · **Tier.** baixo (a sessão longa é da M0.3, tier alto lá) · **Rollback.** revert.

### M4.2 · Som e sinal na cozinha do modo API

**Problema.** No modo API o tablet não toca nada quando um pedido pago entra nem quando atrasa. A dona está
de costas para a tela (D6) e o som é o primeiro passo do UC-04 (`Downloads/03-ANALISE…md:312`).
**Abordagem.** Reaproveitar `useAvisoSonoro` e a regra pura de `dominio/avisoSonoro.js` (retrato antes e
depois, sem som na primeira carga) sobre a fila do KDS: entrou cartão em Aguardando (pago) = som de caixa
(RN-25) e cartão pisca; virou atrasado (S21) = som de atraso. Mesma preferência de som do balcão.
**Aceite.**
- [ ] Prova: primeira carga com 10 pedidos não toca; pedido novo pago toca uma vez.
- [ ] Atraso toca uma vez por pedido (a trava `AtrasoNotificadoEm`, `Pedido.cs:186`, vale também para o som).
- [ ] Sem permissão de áudio no navegador, a faixa pede um toque para ligar o som.
**Tamanho.** P · **Tier.** baixo · **Rollback.** revert.

### M4.3 · Fila ordenada pelo prazo

**Problema.** A fila sai na ordem de criação (`KdsPedidoQueries.cs:41`). Pedido feito ontem para a janela das
18 h fica acima de pedido feito hoje para as 12 h.
**Abordagem.**
- Chave de ordem única, calculada no backend e devolvida no DTO como `prazoEm`: `ProntoAteEm` (S50, quando existir) → `InicioPrevistoEm` (S21) → início da janela → `CriadoEm`.
- `OrderBy(prazoEm)` na consulta e a tela respeita a ordem dentro de cada coluna; atrasado sempre no topo.
- Se D4-01 escolher "fixar no topo": campo `Pedido.FixadoEm` (migration aditiva) e toque longo no cartão.
**Aceite.**
- [ ] Teste de integração: três pedidos criados fora de ordem saem na ordem do prazo.
- [ ] Pedido sem janela nem agendamento vai para o fim, por criação.
- [ ] A ordem é a mesma na tela e no impresso do lote (S46).
**Testes (Red).** `KdsPedidoQueriesPostgresTests.OrdenaPorPrazo`, `...SemPrazoNoFim`.
**Depende de.** nada; S50 entra depois sem mudar o contrato. **Tamanho.** P · **Tier.** baixo (alto se D4-01 = B) · **Rollback.** revert.

### M4.4 · Tempo real de preparo medido

**Problema.** O sistema promete faixa pelo tempo previsto do item (RN-06, RN-22), mas ninguém sabe quanto o
preparo leva de verdade; a Q2 (capacidade com linha mista) depende disso.
**Abordagem.** Sem migração, lendo o que já é gravado:
- Início real = `OcorridoEm` do evento `aguardando → preparando`; fim = `preparando → pronto` (`AtualizarStatusPedidoUseCase.cs:108-119`).
- Cartão em preparo mostra "em preparo há N min"; ao ficar pronto, guarda "levou N min" no detalhe.
- `GET api/kds/tempos?de&ate` (Operador): por item e por linha, mediana e p90 do real × previsto, número de pedidos. Pedido com mais de um item atribui o tempo ao pedido inteiro, separado por linha (limite declarado).
- Painel simples no M4 ("Tempos") e, quando M2 existir, sugestão de ajuste do `TempoPreparoMinutos` (a dona confirma, D8).
**Aceite.**
- [ ] Teste de use case com eventos sintéticos: mediana e p90 corretos; pedido sem evento de início fica fora.
- [ ] Lote de papel (S46) marcado depois da volta da conexão usa a hora informada no lote, não a do envio (conferir se o lote grava `OcorridoEm` informado; se não grava, o pedido fica fora do cálculo).
- [ ] Isolamento por empresa.
**Testes (Red).** `TemposPreparoQueryTests.MedianaPorItem`, `...SemInicioFicaFora`.
**Tamanho.** M · **Tier.** baixo · **Rollback.** revert; nada persiste.

### M4.5 · O que produzir agora (itens somados)

**Problema.** A tela é por pedido; a dona cozinha por prato ("6 lasanhas, 3 nhoques"). Somar de cabeça
entre cartões é o tipo de atenção contínua que a operação não aguenta (risco "operador único").
**Abordagem.** Faixa "Agora" no topo da fila: soma por item e variação dos pedidos em Aguardando e Em
preparo do dia (e da linha filtrada), em ordem de prazo; toque abre os pedidos daquele item. Calculado na
tela sobre a resposta do KDS (dado já vem, `KdsPedidoQueries.cs:97-106`), regra pura em `dominio/`.
**Aceite.**
- [ ] Prova pura: 3 pedidos com o mesmo prato em variações diferentes somam por variação.
- [ ] Filtro de linha (F12) filtra também a faixa.
- [ ] Cancelado não soma.
**Fora.** Marcar item a item (D4-02); baixa de insumo pela ficha técnica (M2).
**Depende de.** F12 (filtro de linha). **Tamanho.** P · **Tier.** baixo · **Rollback.** revert.

### M4.6 · Amanhã e encomendas na cozinha

**Problema.** A cozinha só enxerga o dia: encomenda de amanhã aparece quando já é amanhã. O backend aceita
`data` (`KdsController.cs:31`), o console não manda (`infra/api/kdsApi.js:6`).
**Abordagem.** Seletor "Hoje · Amanhã · escolher dia" na fila; dia diferente de hoje abre em leitura (sem
toque de status) com a faixa "Agora" da M4.5 virando "Para produzir neste dia". Encomenda vem do M8.2.
**Aceite.**
- [ ] Encomenda para amanhã aparece em "Amanhã" e não em "Hoje".
- [ ] Em dia futuro o botão de status não aparece; a API recusa transição de pedido de outro dia? (medir a regra atual antes; se aceita, a tela só esconde).
- [ ] Teto de 200 por consulta (`KdsPedidoQueries.cs:14`) mostra aviso quando atingido.
**Depende de.** M8.2 para gerar encomenda pelo console; M4.5. **Tamanho.** P · **Tier.** baixo · **Rollback.** revert.

### M4.7 · Comanda e número do dia no cartão

**Problema.** A comanda aprovada (S52) existe na API e a cozinha não tem botão para ela; o número do dia
(S53) é como a cozinha chama o pedido em voz alta.
**Abordagem.** Botão "Comanda" no cartão: `GET api/pedidos/{id}/comanda?modelo=etiqueta-10x15|cupom-58`
(`PedidoImpressoController.cs:40`) pelo mesmo consumidor de impressão da S51 (navegador; 58 mm em imagem
quando a S51 entrar). Número do dia em destaque no cartão quando o campo vier (S53, PR #1293).
**Aceite.**
- [ ] Comanda abre e imprime pelo navegador com o papel salvo por dispositivo.
- [ ] Sem número do dia, o cartão mostra o código curto, como a comanda.
- [ ] 401 desloga como o resto (`chamarApi`, F07 item 10).
**Depende de.** S53 (#1293) e S51. **Tamanho.** P · **Tier.** baixo · **Rollback.** revert.

## 5. Ordem

```
M0.2/M0.3 ─► M4.1 tela de dispositivo ─► M4.2 som
F12 paridade ─► M4.5 itens somados ─► M4.6 amanhã (com M8.2)
M4.3 ordem por prazo (independente; S50 depois sem quebrar)
M4.4 tempo real (independente) ─► alimenta D8-05 capacidade por linha
S53 + S51 ─► M4.7 comanda no cartão          F11 lote de papel segue no plano irmão
```

## 6. Decisões pendentes do Felipe

**D4-01 · Prioridade da fila**
- A) (Recomendado) Só derivada do prazo (pronto até, início previsto, janela): sem campo novo, sem toque a mais.
- B) Derivada do prazo e "fixar no topo" marcado pela dona (migração, tier alto).
- C) Manter a ordem de chegada de hoje.

**D4-02 · Rastreio por item**
- A) (Recomendado) Soma por item para saber o que produzir (M4.5); o status continua por pedido e o papel tem as caixas da comanda.
- B) Marcar cada item feito e embalado no tablet (migração em `PedidoItem`, mais toques por pedido).
- C) Nenhum, só o cartão por pedido.

**D4-03 · Encomendas na cozinha**
- A) (Recomendado) Seletor Hoje/Amanhã/dia, dia futuro só leitura (M4.6).
- B) Só hoje; encomenda aparece no dia.
- C) Visão da semana inteira em colunas por dia.

**D4-04 · Tempo real alimenta o tempo previsto?**
- A) (Recomendado) O painel sugere o ajuste e a dona confirma (D8: nada dispara sem marcação).
- B) Ajuste automático do `TempoPreparoMinutos` pela mediana das últimas semanas.
- C) Só mostrar o painel, sem sugestão.

Sessão longa do tablet não é decisão deste documento: é a D-05 do [01-fundacao.md](01-fundacao.md).
