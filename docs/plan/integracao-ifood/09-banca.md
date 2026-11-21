# 9. Banca revisora

Três revisores independentes, em subagentes que não participaram da produção, tentaram refutar o estudo em 2026-11-16. Relatórios completos em [banca/](banca/).

| Revisor | Foco | Conferido | Resultado |
|---|---|---|---|
| [R1](banca/R1-api.md) | Afirmações sobre a API contra as URLs citadas, no navegador | 33 afirmações | 27 confirmadas, 5 parciais, 1 não verificável, 0 refutadas |
| [R2](banca/R2-consistencia.md) | Rastreabilidade RF, UC, L, D e N; contradições; casos faltando | 20 achados | 5 altos, 10 médios, 5 baixos |
| [R3](banca/R3-easystok.md) | Fatos do EasyStok no código e a arquitetura contra as ADRs | 26 afirmações | 21 confirmadas, 5 parciais, 0 refutadas; 5 ajustes de arquitetura |

## 9.1 Achados e correções

| Achado | Origem | Correção | Onde |
|---|---|---|---|
| Limites de uso indevido incompletos | R1 #32 | Lista completa de 1% e 2%, ack de 95%, polling abaixo de 30 s | 02 §2.7, RNF09 |
| `sponsorshipValues` com duas tabelas na mesma página | R1 #18 | Registrado como divergência; N26 | 02 §2.8, 03 |
| Conclusão automática no agendado e em mercado | R1 #14 | Prazo do horário agendado; 13 h em mercado | 02 §2.4 |
| Retenção de 8 h | R1 #4 | Até 8 h após a entrega, só eventos sem ack | 02 §2.3, RNF04 |
| Motivos 501 a 512 fixos | R1 #15 | Lista dinâmica por pedido; sem 510 | 02 §2.4 |
| Token de 8000 caracteres | R1 #26 | Marcado `[NÃO CONFIRMADO]` (N17) | 02 §2.2 |
| Chat com o cliente | R1 | A doc cita o chat; sem endpoint (N16) | README, 08 |
| Webhook sem ack | R1 | Polling como contingência | 02 §2.3, D04 |
| Divergências novas (homologação do Catalog, 8 min no agendado) | R1 | 02 §2.8 passa de 8 para 11 pontos | 02 §2.8, 07 |
| UC17 forçava concluir pedido em preparo | R2 #1 | Registrar e alertar; operador marca `Pronto`, depois conclui | UC17 |
| Desvínculo com pedido aberto parava o polling | R2 #2 | Estado "desvinculando" no polling até zerar | UC02 |
| Fase 1 usava Merchant e Catalog sem declarar | R2 #3 | Fase 1 inclui leitura de Merchant e Catalog | README |
| CAR, DAR, RPS, DRCR, DGAC sem tratamento | R2 #4 | UC25, UC26; RF26, RF27; UC13 E.5 | 05, 06 |
| Rastreio RF e UC assimétrico | R2 #5 | RF12 com UC21, RF19 com UC02, UC24 com RF06 e RF07; RF29 e RF32 novos | 05, 06 |
| Link para 09 sem arquivo | R2 #6 | Este arquivo | README |
| RF24 contra D11 | R2 #7 | RF24 só se D11=B; fase 3 só preço | README, RF24 |
| D15 sem RF | R2 #8 | RF31 e UC53; L17 em RF07; L15 em RNF05 | 05, 06 |
| Reconciliação e contingência sem RF | R2 #9, #15 | RF30 e UC52 | 05, 06 |
| Exceções faltando no UC11 | R2 #10 | E.8 merchant sem vínculo, E.9 falha permanente, E.10 retirada (UC27, RF28) | 06 |
| Disputa parcial e OPA depois de `Pronto` | R2 #11 | UC19 E.5 (N25); UC20 E.1 e E.3 | 06 |
| Aceite ambíguo | R2 #12 | Aceite é registro; preparo exige aceite (UC14 E.2) | 04 §4.1, UC14 |
| N faltando | R2 #13 | N17 a N27 | 08 §8.4 |
| UC16 despachava sem `Pronto` | R2 #14 | A máquina bloqueia; o KDS exige `Pronto` | UC16 |
| Itens baixos: `TIMEOUT_EVENT`, fase com condição, pausa na fase 1, 401 em cascata | R2 #16, #17, #19, #20 | `metadata` citado; RF20, RF24, RF25 com condição; UC41 com a pausa da fase 1; UC03 E.3 | 05, 06 |
| Nível de acesso no cancelamento | R3 #4 | Nível Gerente só com pagamento; o comando por sistema pula nível, devolução e saldo | 04 L19, 07, RF13 |
| Baixa "só em Pronto" | R3 #6 | Ao entrar em `{Pronto, SaiuParaEntrega, Entregue}` | 04 L09 |
| Relink #1619 omitido | R3 #7 | Mapear para `CardapioItemId` basta | 04 §4.3, RF05 |
| Host do job | R3 #15, arq. 4 | Worker recomendado; do Lalamove só o padrão de lock e tenant | 07 §7.2 |
| `CodigoMarketplace` existe | R3 #21 | Citado e descartado como código por canal | 04 §4.3 |
| Credencial de plataforma | R3 arq. 1 | Compatível com a ADR-0061; registrar emenda | 07 §7.2 |
| Eco de status | R3 arq. 3 | RNF19 anti-eco por `Origem` | 05, 07 |
| Fila de migração com I6.2 | R3 arq. 2 | Ordem a decidir com o Felipe | 07 §7.2 |
| ADRs como regra | R3 arq. 5 | ADR-0010, I11.2 e ADR-0061 D1 citadas | 07 §7.2 |

## 9.2 Não corrigido, com motivo

| Achado | Motivo |
|---|---|
| R2 #18, ordem das D em 08 | Ordem por tema é intencional (negócio, operação, técnica) |
| R1, páginas não abertas (G/first-steps, G-cat/fundamentals, G-cat/workflow) | Fora do tempo da banca; ficam valendo pela leitura original em [fontes/](fontes/) e entram no spike |
