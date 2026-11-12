# Sala de guerra do APK Casa da Babá (#2305)

Laboratório isolado (`scripts/homologacao/laboratorio.ps1`): API e Postgres só em loopback, Mercado Pago falso, APK de laboratório no emulador `CasaDaBabaReview` apontando para a API do laboratório. Nada foi testado contra produção. Registro de 2026-11-12.

Estado: OK passou; CORRIGIDO tem PR com teste que falha antes; ABERTO segue na issue; NAO-BUG refutado com prova na issue; NAO-TESTADO ficou de fora.

## Rodada 1 (APK, PWA, console e API)

| Módulo | Achados | Sev | Estado | Evidência |
|---|---|---|---|---|
| Navegação | Rótulos e navegação do APK | | CORRIGIDO | PR #2323 |
| Navegação | CLI-9: tela Clientes sem acesso visível no APK | S2 | CORRIGIDO | PR #2330 |
| Sincronização | SYNC-3 produto inativo volta ao app; SYNC-4 semente duplica o catálogo; SYNC-7 contador de pendentes | S2 | CORRIGIDO | PR #2328, closes #2316 |
| Sincronização | SYNC-8 modal de conflito com `c.id`; SYNC-9 erro transitório vira rejeição; id de pedido por `Date.now()` | S2 | CORRIGIDO | PR #2340, refs #2317 |
| Sincronização | SYNC-6 cursor travado (suspeita); SYNC-10 id do lote por `Date.now()` | S1 se confirmar | ABERTO | #2317 |
| Sincronização | Produto ERP alterado não reenviado no pull | | CORRIGIDO | PR #2312 |
| Sincronização | Pedido pago não aparece no app | | CORRIGIDO | PR #2313 |
| Atendimento | Tomar conversa de outra atendente sem confirmar | | CORRIGIDO | PR #2310 |
| Atendimento | ATD-2 origem do pedido; ATD-5 pedido pago aguardando; ATD-6 prévia "Você:" | S3 | CORRIGIDO | PR #2339, closes #2320 |
| Caixa | Método, casas decimais, teto e autor do lançamento | | CORRIGIDO | PR #2311, PR #2325 |
| Caixa | CAIXA-4 lançamento sem caixa aberto; CAIXA-7 fechar caixa local do PWA | S3 | CORRIGIDO | PR #2327, closes #2319 |
| Estoque | PROD-2 estoque soma lojas; PROD-4 divergência por lote único; SYNC-5 PATCH de produto sempre 409 | S2 | CORRIGIDO | PR #2332, refs #2318 |
| Estoque | PROD-3 "Estoque do dia" do console | S2 | ABERTO | #2318 |
| Produção | PROD-5 quantidade e peso inteiros | S3 | NAO-BUG | #2318 |
| Clientes | CLI-3 edição do console não chega ao app; CLI-5 CPF/CNPJ não sai do aparelho | S1 | CORRIGIDO | PR #2331 |
| Clientes | CLI-4 regra de impressão sem chave (401) | S2 | CORRIGIDO | PR #2330 |
| Clientes | Vínculo pelo telefone normalizado | | CORRIGIDO | PR #2322 |
| Console | Sincronização, estoque, autoria e usabilidade do console | | ABERTO | PR #2338 |

## Rodada 2 (API do laboratório)

| Módulo | Achados | Sev | Estado | Evidência |
|---|---|---|---|---|
| Caixa | R2-API-1: pix de pedido da loja abre caixa fantasma com o Caixa da empresa aberto | S1 | CORRIGIDO | PR #2345, refs #2341 |
| Caixa | R2-API-2 lançamento sem caixa aberto | S2 | CORRIGIDO | PR #2327 |
| Caixa | R2-API-3 data do lançamento vinda do corpo; R2-API-10 saída sem teto no sync; R2-API-12 cancelar pedido pago mantém o pagamento | S2/S3 | ABERTO | #2341 |
| Produção | R2-API-7 porções sem teto travam a API; R2-API-8 loja inexistente dá 500 | S2/S3 | CORRIGIDO | PR #2346, refs #2343 |
| Sincronização | R2-API-11 `order.upsert` incompleto dá NullReference | S3 | CORRIGIDO | PR #2346 |
| Sincronização | R2-API-9 corrida de insert do mesmo id (409) | S3 | ABERTO | #2343 |
| Atendimento | Conversa do chat do site nasce sem aguardar a equipe | S3 | NAO-BUG | #2344 |
| Lojas | Operador e aparelho acessam outra loja | | ABERTO, decisão do Felipe | #2342 |

## Rodada 2 (APK no emulador)

| Módulo | Cenário | Sev | Estado | Evidência |
|---|---|---|---|---|
| Navegação | Início, Produção, Pedidos, Clientes, Histórico e Finalizados sem erro JS, 4xx/5xx ou tela branca | | OK | Testador R2 |
| Navegação | Voltar do Android: Pedidos volta ao Início; Início sai para o launcher | | OK | Testador R2 |
| Clientes | Cadastro no app chega ao servidor (nome, endereço, telefone) | | OK | Testador R2 |
| Sincronização | Modo avião: 2 clientes offline, fila zera ao religar, sem duplicata | | OK | Testador R2 |
| Sincronização | R2-APK-7: mudança feita no servidor não chega ao app sem nova mutação | S3 | CORRIGIDO | PR #2350, closes #2347 |
| Sincronização | R2-APK-1 fila parada sem sessão Google; R2-APK-3 catálogo vazio após parear | S2 | NAO-BUG | #2348 (build piloto exige sessão Google) |
| Sincronização | R2-APK-2 parear de novo com outra empresa lança exceção sem aviso | S2 | ABERTO | #2348 |
| Clientes | R2-APK-6 telefone do app gravado cru no servidor | S3 | ABERTO | #2348 |
| Estoque | R2-APK-4 produto criado no app fora de `/api/produtos` (suspeita) | S2 | ABERTO | #2348 |
| Pedidos | R2-APK-5 pedido do app fora de `/api/pedidos` (suspeita); R2-APK-8 `console.error` no `saveOrder` | S3 | ABERTO | #2348 |
| Estoque e produto | Ressalvas de estoque por loja e produto inativo | | ABERTO | #2349 |
| Caixa, estoque, produção | Venda no caixa, entrada e baixa de estoque e registro de produção pelo APK | | NAO-TESTADO | Caixa e produção cobertos pela API na rodada 2 |
| Navegação | Rotação de tela | | NAO-TESTADO | |

## Fechamento

Merge e publicação: as PRs da rodada 2 entram juntas; a API, o console e o OTA seq 24 saem do SHA final e são conferidos por `buildSha`.
