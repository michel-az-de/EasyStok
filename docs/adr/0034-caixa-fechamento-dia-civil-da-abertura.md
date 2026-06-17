# ADR-0034 — Caixa: fechamento atribuído ao dia civil da abertura (sessão cross-day)

**Status:** Aceito
**Data:** 2026-06-17
**Refs:** issue #640 (fechar caixa de dia anterior bloqueava abrir o de hoje)

## Contexto

O caixa do EasyStok não tem entidade "sessão": o estado é uma sequência de `MovimentoCaixa`
("abertura"/"fechamento"/"entrada"/"saida") mais um snapshot `FechamentoCaixa` chaveado por
`(EmpresaId, Data, LojaId)`. Uma "sessão aberta" é a última abertura sem fechamento posterior
(`GetAberturaPendenteAsync`, #596). Como o operador pode esquecer de fechar, uma sessão pode
atravessar a meia-noite e abranger vários dias civis.

O bug #640: ao fechar uma sessão esquecida de um dia anterior, o `FecharCaixaUseCase` datava o
snapshot em **hoje** (`cmd.Data ?? Hoje()`), agregava a janela errada e fazia o `AbrirCaixaUseCase`
encontrar "um fechamento de hoje" — bloqueando a abertura do caixa de hoje ("como se eu tivesse
fechado o de hoje"). A correção precisa datar o fechamento no dia certo. Surge então a pergunta de
produto: **a quem pertencem as transações de uma sessão que cruzou a meia-noite?**

## Decisão

A sessão é atribuída **só ao dia civil da abertura** (fuso de Brasília, `HorarioBrasil`).

1. **Dia-alvo = dia civil da abertura.** `FecharCaixaUseCase` resolve a sessão via
   `GetAberturaPendenteAsync` (server-authoritative) e data o `FechamentoCaixa` em
   `DataOperacional(abertura.DataMovimento)`. Fechar uma sessão de ontem grava o snapshot em
   ontem e libera o caixa de hoje.
2. **Totais = janela civil do dia da abertura.** Agrega `GetMovimentosDoDiaAsync(dia)` etc. —
   transações pós-meia-noite (dia civil seguinte) **não** entram nesse fechamento; pertencem ao
   caixa do dia em que ocorreram (são capturadas quando aquele dia for aberto/fechado).
3. **Dias intermediários geram aviso, não silêncio.** Numa sessão de 3+ dias, os dias civis
   *entre* a abertura e hoje não têm abertura própria nem fechamento automático. O use case
   detecta lançamentos nesse intervalo e **anexa um aviso persistido em
   `FechamentoCaixa.Observacoes`** + log (não descarta em silêncio).
4. **Sem sessão aberta, não fabrica fechamento.** Guard contra corrida/forja que gravaria um
   fechamento de hoje sem abertura (reintroduziria o bug). Só honra idempotência de snapshot
   existente.
5. **Integridade no banco.** Índice único coalescido `(EmpresaId, COALESCE(LojaId,'0…'), Data)`
   em `fechamentos_caixa` (o unique do EF não dedupa LojaId nulo, NULL≠NULL) fecha a corrida de
   duplo-fechamento empresa-level.

## Motivação / trade-off

Alternativas consideradas: (a) **sessão inteira → dia da abertura** (um fechamento cobrindo
abertura→fechamento, cruzando dias) — elimina órfãos mas funde múltiplos dias num só snapshot,
distorcendo relatório diário; (b) **auto-split por dia civil** (um fechamento sintético por dia) —
contábil-perfeito mas exige fechamentos retroativos automáticos (parente do auto-fechamento, fora
de escopo) e é complexo. Escolhemos "só o dia civil da abertura" por manter a semântica de dia
civil (relatórios por dia) e o fix mínimo, **assumindo conscientemente** a consequência abaixo.

## Consequência (lacuna conhecida) + runbook

Numa sessão que ficou aberta por **3+ dias civis**, os dias **intermediários** não são cobertos
automaticamente por nenhum `FechamentoCaixa` (a abertura é do 1º dia; o fechamento manual cobre só
esse dia; hoje cobre o caixa de hoje). O use case **avisa** (Observacoes + log), mas a
reconciliação é **manual**:

- **Runbook:** para cada dia intermediário com lançamentos, lançar um movimento de ajuste no caixa
  daquele dia (ou abrir/fechar retroativamente o dia via `cmd.Data` na API, que valida contra a
  sessão), de modo que cada dia civil tenha seu próprio fechamento.
- O caso comum (esquecer de fechar de um dia para o outro, sessão de 1 noite) **não** tem dias
  intermediários — o aviso fica vazio e nada precisa ser reconciliado.

Se o volume de sessões multi-dia crescer, reabrir a decisão para auto-split (alternativa b).
