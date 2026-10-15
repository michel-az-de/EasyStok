# M8 Entregas · janelas, áreas, entregadores, viagens, rota e chamados

Issue: #1316 · Decisão: [ADR-0056](../../adr/0056-erp-da-casa-da-baba-front-unico.md) item 3 (módulo novo,
não estava no rascunho) · Data: 2026-10-01 · Base medida: master `b713263a`.
Plano irmão (dono de S01–S53 e F01–F18): [10-console.md](../atendimento-whatsapp/10-console.md),
[11-console-fechamento.md](../atendimento-whatsapp/11-console-fechamento.md). Fundação:
[01-fundacao.md](01-fundacao.md) (M0.2 rotas `#/m/entregas/painel` = `#/entregas` e
o entregador não loga: D8-01 decidiu link por viagem, sem perfil Entregador). Este documento não
reescreve S nem F.

## 1. Veredito

> As seções 1 a 6 conservam o levantamento histórico de 01/10/2026 e decisões datadas posteriores.
> Para o laboratório de cotação, contratação e caixa da issue #1693, consulte a seção 7.
> Implementação local, prova com HTTP controlado, integração real e publicação são evidências diferentes.

**O BACKEND DE ENTREGAS ESTÁ COMPLETO PARA O DIA (S12, S14, S16, S44, S45) E A TELA DA DONA ESTÁ LIGADA (F04). FALTAM QUATRO COISAS: ENCOMENDA PARA OUTRO DIA NA TELA, ENDEREÇO DE ENTREGA NO PRÓPRIO PEDIDO, A TELA DO ENTREGADOR E A LALAMOVE (F17).**

| Medida | Valor | Fonte |
|---|---|---|
| Fatias mergeadas | S12, S13, S14, S16, S44, S45, #1219 (geocodificação), #1278 (Routes API no frete por raio); F04, F08 | `gh pr list` (#1123, #1143, #1140, #1149, #1193, #1096, #1219, #1278, #1222, #1266) |
| PR aberta | F16 integrações (#1304), pré-requisito da F17 | `gh pr list` |
| Sem PR | F12 paridade visual (#1242), F17 Lalamove e rota (#1247) | `gh issue view` |
| Janela | modelo semanal (dia da semana + horário + capacidade) com vaga por **data** | `Entities/Storefront/JanelaEntrega.cs:4,25,31`; `VagaOcupada.cs:25` |
| Horizonte da oferta de janelas | 14 dias por padrão, 60 no máximo; a comanda mostra só as **10 primeiras** com vaga | `ListarJanelasDisponiveisUseCase.cs:43-44`; `ListarJanelasAtendimentoUseCase.cs:32,45` |
| Endereço da entrega | vem do **cadastro do cliente**, não do pedido (`Pedido` só tem nome, apto e telefone) | `KdsPedidoQueries.cs:58-67`; `Pedido.cs:41-43` |
| Entregador | sem vínculo com usuário: não há como "ele" entrar no sistema | `Entities/Atendimento/Entregador.cs:16-26` |
| Fatias novas deste documento | **6** (M8.1 a M8.6) | §4 |

## 2. Como o M8 vive dentro do shell

| Tela | Quem | Rota (M0.2) | Dispositivo |
|---|---|---|---|
| Painel do dia (hoje: gaveta e `TelaEntregasApi`) | Dona, Atendimento | `#/m/entregas/painel` (apelido `#/entregas`) | desktop ou tablet, aberta também pelo trilho do M3 (↗) |
| Agenda (encomendas por dia) | Dona, Atendimento | `#/m/entregas/agenda` | desktop |
| Cadastros: janelas e áreas, entregadores | Dona (policy Admin hoje nas janelas e zonas) | `#/m/entregas/janelas`, `#/m/entregas/entregadores` | desktop |
| Relatórios | Dona | `#/m/entregas/relatorios` | desktop |
| Minhas viagens | Entregador (motoboy próprio) | `#/m/entregas/minhas-viagens` ou link por viagem (D8-01) | celular |

```
 Dona/Atendimento                                Entregador (celular)
 #/m/entregas/painel ── viagem: paradas, ordem ──► link ou login ──► Minhas viagens
        │  "Saiu para entrega" (RN-32)                 │ abrir no Maps · ligar · "Entregue"
        ▼                                              ▼
 Pedido: pronto → saiu_para_entrega → entregue  ◄──────┘   (Lalamove: webhook da F17)
```

## 3. Mapa: sub-tela → fatia que cobre → estado

Legenda: ✅ mergeada · 🟡 PR aberta · ⬜ sem PR · ❌ sem fatia (vira M8.x).

### 3.1 Janelas e áreas

| Peça | Backend | Console | Estado | Lacuna |
|---|---|---|---|---|
| Janela com capacidade e prazo mínimo dos itens (RN-21, D9) | S16 #1149 | F03 (comanda), F04 | ✅ | nenhuma |
| Cadastro de janelas, zonas e bloqueios, 60 dias | S45 #1096 (`TenantVitrineEntregaController.cs:37-91`) | F04 (criar, ativar, desativar) | ✅ | editar o já criado: **M8.6** |
| Área por faixa de CEP ou lista de bairros | S14 #1140 (`FreteZona.cs:63`, `cep_range` ou `bairros_lista`) | F04 | ✅ | nenhuma |
| Frete por raio com distância real de rota | #1219, #1278 (`Storefront.cs:89` `FreteRaioMaxMetros`; `GoogleRotasClient.cs:36`) | a medir | ✅ backend | tela de configuração: **M8.6** |
| Aprovar ou recusar fora da área (UC-02) | S12, #1196; F08 tirou o corte de data | F04 (tela Entregas) | ✅ | na ficha: M3.3 |
| Aba "Janelas" duplicada na Gestão | não | F06 reusa "Janelas e frete" | ✅ | sai da Gestão no M3.1 |
| Capacidade considerando a linha do produto (US-027) | não | não | ⬜ | questão aberta Q2: **D8-05** |

### 3.2 Agendamento e encomenda

| Peça | Fato | Estado | Lacuna |
|---|---|---|---|
| Backend aceita data futura | vaga por `DataEntrega` (`VagaOcupada.cs:25`); oferta de 14 a 60 dias (`ListarJanelasDisponiveisUseCase.cs:43-44`) | ✅ | nenhuma |
| Comanda no modo API | janela com data e lotação do servidor (`features/ficha-cliente/SeletorJanelaApi.jsx:7-9`), mas sem escolher o dia: só as 10 primeiras com vaga (`ListarJanelasAtendimentoUseCase.cs:32,45`) | 🟡 parcial | **M8.2** |
| Protótipo e modo massa | oferta só do dia de hoje (`dominio/entrega.js:201` `ocupacaoDeHoje`), inclusive no "Alterar agendamento" (`features/entregas/ModalAlterarAgendamento.jsx`) | ⬜ | **M8.2** |
| Ver as encomendas dos próximos dias | não existe tela; o KDS aceita `data` (`KdsController.cs:31`) | ❌ | **M8.2** (agenda) e M4.6 (cozinha) |
| Trocar a janela de pedido criado | reagendar não move a vaga (`AlterarAgendamentoPedidoUseCase.cs:41-42`) | ❌ | **M3.4** |
| Lembrete da encomenda | job só sobre `mobile_orders` (`AgendamentoNotificacaoService.cs:74-89`) | ❌ | **M3.8** |

Fato da entrevista: "Agendamento, não pronta entrega" (D9, `Downloads/03-ANALISE…md:116-118`), e o caso
real da cliente "que queria delivery e ficou como encomenda agendada" (`Downloads/02-ESTORIAS…md:288`). A
US-026 fala em "janelas configuradas para o dia" (`02-ESTORIAS…md:772`). **Inferência:** a encomenda para
outro dia é caso real, não hipótese; o protótipo só não a desenhou.

### 3.3 Entregadores, viagens, rota e chamados

| Peça | Backend | Console | Estado | Lacuna |
|---|---|---|---|---|
| Cadastro de entregador (próprio, 99, Lalamove, iFood, outra) | S44 (`AtendimentoEntregasController.cs:26,40,49,64`; `EmpresaEntregador.cs:9-13`) | F04 (cadastrar, desativar) | ✅ | editar: **M8.6** |
| Viagem: montar, pôr pedido, ordem, entregador, sair, entregue, desfazer | S44 (`AtendimentoEntregasController.cs:105-155`) | F04 | ✅ | arrastar: fora do go-live (30/09) |
| Um pedido em uma viagem ativa só | F08 item 4 | nenhuma | ✅ | nenhuma |
| Rota: link do Maps | `RotaMaps.cs:11` (só monta o link, não chama API) | F04 | ✅ | ordenar pela rota: F17 |
| Chamado de entregador em texto livre | S44 (`AtendimentoEntregasController.cs:161-176`) | F04 | ✅ | nenhuma |
| Lalamove: cotar, chamar, cancelar, acompanhar, webhook (US-043, US-044) | não existe | F17 #1247 | ⬜ | depende da F16 (#1304) |
| Chips, ordenação, resumo do dia, cartão do protótipo, "Conferi" | dados já vêm | F12 #1242 | ⬜ | nenhuma |
| Gaveta do balcão fazendo viagem "por aqui" | avisa "use a gaveta Entregas" (`naoLigadas.js:42-46`) | F04 | ✅ por desenho | nenhuma |
| **Tela do entregador no celular** | não há rota para o entregador ler a viagem dele nem marcar entregue | não existe | ❌ | **M8.4** |
| Endereço e coordenadas da entrega no pedido | `Pedido` sem endereço; a F17 supõe "coordenadas gravadas no endereço de entrega do pedido na captura (S14)" (`11-console-fechamento.md:399`) | não existe | ❌ | **M8.3** (pré-requisito da F17) |

### 3.4 Relatórios e impressos

| Peça | Backend | Console | Estado | Lacuna |
|---|---|---|---|---|
| Entregas por bairro | `GET api/atendimento/relatorios/entregas-por-bairro` (`AtendimentoEntregasController.cs:211`) | fora da F04 | ⬜ | **M8.5** |
| Pontualidade (entregue dentro da faixa prometida, RN-22) | dados existem: `ParadaViagem.EntregueEm` (`ParadaViagem.cs:16`), `Viagem.SaiuEm` (`Viagem.cs:19`), janela da vaga | não existe | ❌ | **M8.5** |
| Recibo de envio 10×15 | backlog dos impressos, item 4 (`12-impressos.md:176`) | nenhuma | ⬜ | dono: `10-site-e-impressos.md` |

## 4. Fatias novas

Convenções do [README §4](README.md); aceite fixo "sem `VITE_FONTE_DADOS`, o console abre como hoje".

### M8.1 · Entregas como módulo no shell

**Problema.** Entregas hoje é gaveta do balcão e uma janela (`#/entregas`) com abas internas; no ERP ela é
módulo próprio com cadastros e relatórios, e o painel do dia não pode piorar para quem está no balcão.
**Abordagem.**
- Menu do M8 (M0.2): Painel, Agenda, Viagens, Entregadores, Janelas e áreas, Relatórios.
- Painel = `TelaEntregasApi` de hoje, sem mudança de comportamento; as abas "Entregadores" e "Janelas e frete" viram itens do menu.
- O trilho do M3 continua abrindo o painel em janela própria; a gaveta do balcão segue como resumo.
**Aceite.**
- [ ] `#/entregas` abre o mesmo painel de hoje; `#/m/entregas/painel` também.
- [ ] Perfil sem M8 recebe 403 da API em `api/atendimento/viagens` (M0.3); a tela só esconde.
- [ ] Captura do painel em 1440 e 390 px igual ao master, exceto o menu.
**Depende de.** M0.2, M0.3. **Tamanho.** P · **Tier.** baixo · **Rollback.** revert.

### M8.2 · Encomenda para data futura e agenda

**Execução parcial em 10/10/2026.** A comanda escolhe o dia, a agenda de sete dias lê pedidos e vagas ativas por janela, e a API limita oferta, reserva e reagendamento a hoje + 14 dias, conforme D8-02. O limite de 60 dias continua sendo apenas o tamanho máximo da consulta. D8-03 foi implementado e validado localmente: a reserva futura emite link do Mercado Pago para o sinal de 50%, arredondado ao centavo; após a confirmação, o saldo fica pendente para receber na entrega pela Ficha. O pedido só é quitado após o saldo. Homologação com Mercado Pago real permanece pendente.

**Problema original.** A reserva não tinha horizonte futuro e a comanda mostrava apenas as primeiras
janelas com vaga, sem uma agenda dos próximos dias. Encomenda de sábado feita na terça virava conversa e caderno.
**Abordagem.**
- Comanda (`SeletorJanelaApi.jsx`): seletor de dia (hoje, amanhã, calendário até o limite de D8-02) que manda `DataInicio`/`DataFim`, já aceitos por `ListarJanelasAtendimentoInput` (`ListarJanelasAtendimentoUseCase.cs:8-13`); a lista mostra as janelas daquele dia.
- Agenda `#/m/entregas/agenda`: `GET api/atendimento/agenda?de&ate` (Operador), leitura nova que agrega `VagaOcupada` ativa por data e janela (ocupadas de capacidade) e lista os pedidos de cada janela; bloqueios (S45) aparecem como dia fechado.
- O agente (`listar_janelas`) já usa o mesmo use case: só ganha o parâmetro de data, sem regra nova.
- Pagamento segue D8-03 B: sinal de 50% por link na reserva futura e saldo manual na entrega. Reemissão conserva o valor do sinal; confirmação parcial cria a cobrança do saldo sem emitir quitação; a baixa final registra o pagamento no Caixa e emite a confirmação uma vez.
**Aceite.**
- [x] Pedido criado na conversa para daqui a 10 dias ocupa a vaga daquele dia e aparece na agenda (provas locais das seções 31 e 32 do plano de ondas).
- [x] Dia bloqueado não oferece janela; data além do limite volta 400 com o motivo (teste de bloqueio e HTTP local do 15º dia).
- [x] A agenda e a oferta contam a mesma vaga (comparação HTTP local, ADR-0014).
- [x] Encomenda de amanhã aparece em "Amanhã" na cozinha (M4.6) e gera o lembrete da véspera (M3.8, seção 31).
**Testes (Red).** `AgendaEntregasQueryTests.ContaVagasPorJanelaEData`, `...BloqueioFechaDia`; `prova-m8-agenda.mjs`.
**Fora desta fatia.** Recorrência ("toda sexta") e homologação financeira com Mercado Pago real.
**Depende de.** M8.1. **Tamanho.** M · **Tier.** baixo (sem migração) · **Rollback.** revert; a comanda volta às 10 primeiras.

### M8.3 · Endereço de entrega no próprio pedido

**Execução parcial em 11/10/2026.** A criação pela conversa, pelo site (logado e guest, inclusive sem janela) e pela API grava o endereço no pedido. Retirada de balcão mantém os campos nulos. A geocodificação é best-effort pela porta já existente, sem coordenadas inventadas quando indisponível ou imprecisa. KDS, viagens, canhoto, impresso e agregação por bairro preferem o registro do pedido; somente o legado usa o cadastro atual. Migração aditiva validada em PostgreSQL/Testcontainers. O `PATCH` de troca do endereço, a revalidação de cobertura/frete e o aviso operacional no Console ficam para a próxima fatia; M8.3 ainda não está encerrada.

**Problema.** Pedido não guarda para onde vai: KDS e viagem leem o endereço do cadastro do cliente
(`KdsPedidoQueries.cs:58-67`). Se o cliente muda o endereço depois, o pedido de ontem muda junto; não há como
entregar um pedido em outro endereço (`mudarEnderecoDoPedido` avisa, `naoLigadas.js:37`); e a F17 precisa
de coordenadas por parada sem geocodificar a cada cotação.
**Abordagem.**
- Migration aditiva em `pedidos`: `EnderecoEntrega` (texto), `BairroEntrega`, `CepEntrega`, `EntregaLat`, `EntregaLng`, `EnderecoConfirmadoEm`. Retirada no local deixa tudo nulo.
- Gravado na criação pelos três caminhos: pedido da conversa (F03/F18, via `ConfirmarEnderecoClienteUseCase`), checkout do site (S10) e balcão; coordenadas pelo `IGeocodingClient` (#1219), falha de geocodificação não bloqueia (D7, avisa).
- `PATCH api/pedidos/{id}/endereco` (Operador) antes de sair para entrega, revalidando a área (S14) e o frete; fora da área vira aprovação (S12).
- Leitura (KDS, viagens, Minhas viagens, impressos) usa o do pedido e cai no do cliente quando o pedido é antigo.
**Aceite.**
- [x] Mudar o endereço do cliente não muda o endereço de pedido já criado (teste de integração com PostgreSQL, 11/10/2026).
- [ ] Pedido criado pela conversa grava lat/lng quando o Google responde; sem chave, grava o texto e avisa.
- [ ] Trocar o endereço para fora da área põe o pedido em aprovação.
- [ ] Isolamento por empresa no `PATCH`.
**Testes (Red).** `PedidoEnderecoEntregaTests.SnapshotNaCriacao`, `...TrocaForaDaAreaPedeAprovacao`; `KdsPedidoQueriesPostgresTests.EnderecoDoPedidoVence`.
**Depende de.** nada; é pré-requisito da F17. **Tamanho.** M · **Tier.** alto (migration) · **Rollback.** migration `Down`; leitura volta ao cadastro.

### M8.4 · Minhas viagens: a tela do entregador no celular

**Problema.** O motoboy próprio recebe a viagem por mensagem solta; a dona marca "entregue" por ele no fim do
dia (US-044 quer isso automático). `Entregador` não tem usuário (`Entregador.cs:16-26`) e nenhuma rota lê
"a viagem deste entregador".
**Abordagem (D8-01 decidido: opção A).**
- Tela de celular, uma coluna, fonte grande: a viagem atual com as paradas na ordem, para cada uma nome, endereço do pedido (M8.3), apto, telefone (ligar e WhatsApp), faixa prometida, observação de entrega, número do dia para conferir na retirada (S53), botões "Abrir no Maps" (`RotaMaps`) e **"Entregue"** (`POST viagens/{id}/paradas/{pedidoId}/entregue`, `AtendimentoEntregasController.cs:150`). Sem preço, sem histórico do cliente (LGPD: só o necessário para entregar).
- Opção A (link): ao "Saiu para entrega", o EasyStok gera um link da viagem com token de uso limitado, no padrão do link do cardápio da conversa (`LinkCardapioConversa`: `TokenHash`, `ExpiraEm`, `Entities/Atendimento/LinkCardapioConversa.cs:16-21`), e a dona envia ao `Entregador.Telefone` pelo WhatsApp com um toque. Rotas anônimas `GET api/entregas/viagem/{token}` e `POST .../paradas/{pedidoId}/entregue`, só da viagem do token, expiram na conclusão da viagem ou em 12 h; rate limit como o da Meta.
- Opção B (login): descartada em 01/10 (D8-01).
- Entregue pelo entregador dispara o mesmo aviso ao cliente (S13, US-045) e a dona pode desfazer pelo painel.
**Aceite.**
- [ ] Entregador abre a viagem no celular de 390 px e marca a parada entregue; o painel da dona atualiza pelo SSE.
- [ ] Token (A) ou usuário (B) de outra viagem ou empresa não lê nem marca nada (teste de integração).
- [ ] Link vencido mostra "viagem encerrada" sem dado do cliente.
- [ ] Nenhum preço nem dado fora da viagem aparece na resposta (teste de contrato).
**Testes (Red).** `ViagemDoEntregadorTests.SoAViagemDoToken`, `...TokenVencido`, `...SemPreco`.
**Fora.** Rastreio por GPS; foto de comprovante; motorista da Lalamove (usa o app dela, status pela F17).
**Depende de.** M8.3; D8-01. **Tamanho.** M · **Tier.** alto (rota anônima com token, ou migration e perfil) · **Rollback.** revert; revogar os tokens ativos.

### M8.5 · Relatório de entregas: bairro e pontualidade

**Problema.** O relatório por bairro existe na API e não tem tela; pontualidade (entregou dentro da faixa
prometida, RN-22) nunca foi medida, e é ela que diz se o respiro de 40 min está certo.
**Abordagem.**
- Tela `#/m/entregas/relatorios`: por bairro (rota existente, `AtendimentoEntregasController.cs:211`) e pontualidade por período, janela e entregador.
- `GET api/atendimento/relatorios/pontualidade?de&ate` (Admin): para cada parada entregue, `EntregueEm` × fim da faixa prometida (janela da vaga mais respiro); percentual no prazo, atraso mediano, por janela e por entregador.
**Aceite.**
- [ ] Teste com paradas sintéticas: dentro, no limite e fora da faixa.
- [ ] Pedido sem janela não entra no cálculo e aparece como "sem faixa".
- [ ] Operador sem Admin recebe 403 (texto próprio, F13 item 5).
**Tamanho.** P · **Tier.** baixo · **Rollback.** revert.

### M8.6 · Editar janela, área e entregador pelo console

**Problema.** A F04 deixou de fora editar janela, zona e entregador já criados (`10-console.md` F04 "Fora"):
hoje a dona desativa e cria de novo, e perde o histórico de vagas da janela antiga. As rotas de edição já
existem (`TenantVitrineEntregaController.cs:47,69`; `AtendimentoEntregasController.cs:49`).
**Abordagem.** Só console: formulário de edição nas telas de cadastro do M8, com as mesmas validações da
criação devolvidas pela API (`JanelaEntrega.Atualizar`, `JanelaEntrega.cs:76`). Janela com pedidos marcados
pode mudar capacidade e rótulo; mudar dia ou horário avisa quantos pedidos estão nela e não os move (troca
por pedido é M3.4). Frete por raio (`Storefront.ConfigurarFreteRaio`) entra aqui se a medição achar a rota.
**Aceite.**
- [ ] Editar a capacidade de uma janela reflete na oferta da comanda na hora.
- [ ] Mudar horário de janela com pedidos mostra o aviso com a contagem.
- [ ] Editar entregador muda o próximo despacho, não as paradas antigas (snapshot em `ParadaViagem.cs:18-21`).
**Tamanho.** P · **Tier.** baixo · **Rollback.** revert.

## 5. Ordem

```
F16 (#1304) ─► M8.3 endereço no pedido ─► F17 Lalamove e rota (#1247)
M0.2/M0.3 ─► M8.1 módulo ─► M8.2 agenda/encomenda ─► M4.6 cozinha amanhã · M3.8 lembrete
                       └─► M8.6 editar cadastros     M8.3 ─► M8.4 minhas viagens (D8-01)
F12 paridade do painel (plano irmão) · M8.5 relatórios (independente)
```

## 6. Decisões pendentes do Felipe

**D8-01 · Como o entregador entra na tela dele: DECIDIDO (01/10), opção A (link por viagem pelo WhatsApp, sem login; não existe perfil Entregador).**
- A) (Recomendado) Link por viagem mandado pelo WhatsApp, sem login: o entregador é terceiro (persona, `02-ESTORIAS…md:38`), troca muito, e o padrão de link com token já existe (S48). O perfil Entregador da M0.3 deixa de ser necessário.
- B) Perfil Entregador com login e "Minhas viagens", como no diagrama do README e na matriz da M0.3.
- C) Os dois: link agora, login para quem for fixo da casa.

**D8-02 · Até quantos dias à frente a encomenda pode ser marcada**
- **Decidido por Felipe em 10/10/2026: C, 14 dias.**
- A) (Recomendado) 60 dias, o limite que o código já impõe.
- B) 30 dias.
- C) 14 dias, o padrão atual da oferta.

**D8-03 · Pagamento da encomenda**
- **Decidido por Felipe em 10/10/2026: B, sinal de 50% na reserva e restante na entrega. Implementação e validação locais concluídas; homologação externa pendente.**
- A) (Recomendado) Igual ao pedido do dia: pedido e cobrança na hora (RN-23), link reemitido pelo job da S11.
- B) Sinal na hora e o resto perto da data (cobrança em duas partes, fatia nova).
- C) Cobrar só perto da data, com a vaga reservada sem pagamento.

**D8-04 · Quem marca "entregue" quando o entregador é da casa**
- A) (Recomendado) O entregador na tela dele (M8.4); a dona pode desfazer no painel.
- B) Só a dona, como hoje.

**D8-05 · Capacidade da janela com as duas linhas (US-027, Q2)**
- A) (Recomendado) Continua contando pedidos até a M4.4 medir os tempos reais por linha; depois decidir com número.
- B) Capacidade separada por linha já (para servir × preparar em casa), migration em `JanelaEntrega`.
- C) Capacidade em minutos de preparo, somando o tempo previsto dos itens.

## 7. Sprint de logística #1693, 12/10/2026

### 7.1 Objetivo, decisão e limite operacional

Thatiane deve escolher o transporte dos pedidos de uma janela, acompanhar a entrega e conferir seu custo
no Caixa sem sair do contexto de Entregas ou da Ficha do Atendimento. O sprint adotou uma **empresa de
demonstração isolada**, autorizada por Felipe, com pedidos e movimentos persistidos no backend. O acesso
ao laboratório exige que a empresa do token corresponda à configuração `Logistica:EmpresaDemonstracaoId`.
Os adapters externos usam respostas simuladas; a tela só mostra cotação ou serviço simulado quando recebe
esse estado da API. Não há chamada de motorista externo, cobrança de cartão ou envio de WhatsApp real
demonstrados por este laboratório.

O backend usa cotação persistida e expiração, contrato de despacho, idempotência, validação da janela e
endereço, estados de entrega e referência ao movimento de saída no Caixa. O preço do transportador é
separado do frete cobrado do cliente. Transporte combinado manualmente e entrega em mãos fazem registros
locais na empresa demonstrativa, sem fingir integração com o prestador.

### 7.2 Sprint e banca simulada

| Etapa | Trabalho e decisão |
|---|---|
| Entender | Inspeção de Entregas API, viagens, janelas, Ficha e caixa. A tela existente exigia criar viagem vazia e depois adicionar pedidos. |
| Esboçar | Entrada por janela e por pedido; comparação de preço/prazo; confirmação explícita; acompanhamento e financeiro no mesmo modal. |
| Decidir | Reutilizar moldura, componentes, cores e comportamento de Atendimento. Nenhuma dependência de UI adicionada. |
| Prototipar | `ModalFrete` compartilhado, `useFretesApi`, endpoints de logística e empresa demonstrativa persistida. |
| Testar | Banca de personas simuladas, cenários negativos, testes de aplicação/integração e navegador em camadas separadas. |

Os papéis de UX, design/direção de arte, front-end, backend, arquitetura, gerência de projeto e QA são
perspectivas de revisão da simulação. As falas abaixo são **encenadas**, não entrevistas ou aprovação
humana de Thatiane ou de uma funcionária.

| Dificuldade da persona simulada | Resposta de UX implementada |
|---|---|
| “Janela é a hora que sai?” | Texto “Receber entre…” no pedido; estado de coleta separado. |
| “Onde eu começo?” | “Cotar esta janela”, “Organizar transportes” e “Cotar ou acompanhar entrega” na Ficha. |
| “Esse valor é o frete que cobrei?” | Explicação junto às cotações: custo do transportador separado do frete do cliente. |
| “Cliquei e demorou; clico outra vez?” | Botões ocupados, mensagem de confirmação e chave estável para repetir a mesma intenção. |
| “Cadê o preço depois de escolher dez pedidos?” | Lista de seleção recolhe ao apresentar cotações; também recolhe para mostrar acompanhamento. |
| “O produto voltou; já devolveu dinheiro?” | Retorno físico separado do reembolso financeiro na Ficha. |
| “Posso pôr outro pedido nessa entrega?” | Agrupamento antes da contratação; a interface informa que não altera despacho já contratado. |
| “Posso pagar ou ligar o automático como atendente?” | Pagamento exige capacidade `gerenciarCaixa`; política e seed exigem `editarAtendimento`, além dos guards da API. |

Direção de arte: hierarquia curta, uma ação principal por etapa, cards de comparação, preços em destaque,
cores semânticas e trilha discreta de andamento. A animação de confirmação respeita redução de movimento.
Não há mapa fictício, pontuação inventada, economia estimada sem base ou promessa de “melhor rota”.

### 7.3 Dia demonstrativo com dez pedidos

O seeder cria estes pedidos sintéticos, com cinco janelas, endereços e contatos de teste. São dados de
ensaio, não clientes reais. A ordem abaixo descreve o roteiro de teste; não declara que todos os casos
tenham recebido aceite humano ou sido executados no serviço externo.

| Pedido | Destino sintético | Recebimento | Situação e ensaio |
|---|---|---|---|
| 01 Ana | Centro A | 10h–11h | Pronto; cotar e agrupar com Bruno e Carla. |
| 02 Bruno | Centro B | 10h–11h | Pronto; mesma contratação agrupada e uma saída de frete. |
| 03 Carla | Centro C | 10h–11h | Pronto; acompanhar junto aos outros destinos. |
| 04 Diego | Centro D | 10h–11h | Pedido adicional; pode compor nova cotação antes de contratar, não editar contratação existente. |
| 05 Elisa | Norte A | 11h30–12h30 | Sem endereço/coordenadas; bloquear cotação com erro acionável. |
| 06 Fábio | Norte B | 11h30–12h30 | Em preparo; cotação não equivale a autorização para sair. Saída exige pedido pronto. |
| 07 Gabi | Sul A | 14h–15h | Ensaiar entregador combinado, custo digitado e pagamento confirmado. |
| 08 Hugo | Sul B | 14h–15h | Ensaiar entrega em mãos, custo zero e ausência de saída financeira fictícia. |
| 09 Iris | Leste A | 16h–17h | Ensaiar cliente ausente, falha, retorno e confirmação de devolução física. |
| 10 João | Oeste A | 17h–18h | Ensaiar escolha automática desativada e depois limitada pela política e pelo teto. |

Não agrupar horários incompatíveis. Os pedidos contratados devem continuar visíveis no acompanhamento;
devolução concluída permite nova contratação, enquanto pedido entregue não volta a ser elegível.

### 7.4 Contrato de operação e automação

Base: `/api/atendimento/logistica`. Contexto aceita dia ou pedido; cotação recebe IDs de pedidos e decisão
de agrupar; contratação referencia a cotação persistida. Pagamento e eventos recebem chave de idempotência.
Atualização de política e preparação dos dez pedidos são administrativas.

O modo automático só contrata quando a política está habilitada, respeita o teto e a permissão de agrupar,
e escolhe a opção elegível de menor custo. Entrega manual/em mãos exige decisão humana. Trata-se de regra
determinística, não decisão de IA generativa. A ordenação atual usa proximidade entre coordenadas;
**não consulta trânsito nem prova a rota ótima**, e a duração fornecida pelo simulador não é ETA real.

Estados usados: `agendado`, `a_caminho`, `entregue`, `falha`, `devolvendo`, `devolvido` e `cancelado`.
Pagamento significa **valor já pago**, informado com meio e referência; registrar não cobra cartão.
Cancelar ou devolver transporte não estorna automaticamente o pagamento do cliente nem o do entregador.

O acompanhamento público próprio da demonstração usa link HMAC com validade de sete dias. Não exige
sessão, não expõe nome, endereço, telefone ou valores e recusa assinatura adulterada. A interface o chama
de “Rastreio de demonstração”; não é link do transportador e não comprova notificações por WhatsApp.
O despacho é vinculado à viagem canônica; ações tradicionais de edição dessa viagem são bloqueadas
para preservar os pedidos e a ordem da cotação contratada. A Ficha consulta colegas do dia após validar
o pedido/empresa, permitindo selecionar outros pedidos antes de contratar.

### 7.5 Evidências e sala de guerra

| Camada | Evidência disponível nesta atualização |
|---|---|
| Console | Build Vite, lint focado sem avisos/erros, fronteiras entre camadas e 264 pares de contraste aprovados. |
| Navegador com HTTP controlado | `ferramentas/prova-1693-frete-navegador.mjs`: dez pedidos, cotação, custo manual em centavos, negativo bloqueado, repetição da mesma chave após resposta perdida, pagamento, saída/falha/retorno, celular, Escape/foco e restrições de atendente. |
| Inspeção visual | Capturas desktop e celular em `.build/1693-ux/`, revisadas; sem transbordamento horizontal no celular. |
| Navegador com API/PostgreSQL reais | `ferramentas/prova-1693-frete-api-real.mjs` executado sem interceptação HTTP: Diego/P04 cotado, contratado, pago e entregue. API confirmou saída única no Caixa, status no pedido/despacho e persistência após recarregar. |
| Dez cenários HTTP reais | `ferramentas/prova-1693-dez-cenarios-api-real.mjs` passou com PostgreSQL real: grupo de três, P04 pelo navegador, P05 endereço400, P06 preparo bloqueado e KDS pronto, P07 manual R$18, P08 mãos a zero sem gasto, P09 falha/retorno/pedido pronto e P10 automático desligado/ligado dentro do teto. Pagamentos repetidos concorrentemente geraram uma saída por frete. |
| Rastreio público próprio | Página aberta em navegador sem sessão; status entregue, ausência de nome/endereço/valores e token adulterado404 conferidos. Captura em `.build/1693-api-real/rastreio-publico.png`. |
| Refinamento após inspeção real | Lista recolhe e foco passa às cotações mesmo quando foi expandida manualmente. Prova somente de cotação reexecutada, sem nova contratação/pagamento/evento; desktop/celular sem overflow. |
| Publicação | PR #1700 com CI verde, publicado em 13/10/2026 UTC no SHA `1d28b79a` conferido na API e Console. Login real na empresa isolada, dez pedidos do dia 13/10, grupo de três entregue e uma saída de R$24 no Caixa, repetição idempotente e rastreio público sem dados pessoais comprovados. Aceite humano ainda pendente. |

O script com HTTP controlado verifica UX e integração do Console com o contrato; não substitui testes da
API, transações PostgreSQL, isolamento de empresa ou serviços externos. A prova com API real usa
autenticação de teste e simuladores exclusivamente nas integrações externas; também não equivale ao login
e à operação de produção.

Evidências dessa execução: `.build/1693-api-real/resultado.json`, `dez-cenarios.json`,
`desktop-cotacao-foco.png`, `celular-cotacao-foco.png`, `celular-entregue.png` e `rastreio-publico.png`.
Dia sintético ensaiado: **14/10/2026**. No fluxo de navegador, despacho
`7b92dd18-112a-4cbb-b276-f051af15d44c`, valor R$16,00, associado ao movimento
`1a447b3c-1f1c-4477-8529-4042ffdd305b`. São IDs da fixture local descartável, não registros de produção.
O primeiro ensaio do navegador terminou no prazo de carregamento do Vite sob build concorrente, antes
de qualquer mutação; a navegação foi ajustada para DOMContentLoaded e reexecutada com sucesso.

Bloqueadores da banca técnica: contratação/caixa duplicados, vazamento entre empresas, automático fora da
política, pagamento sem autorização ou estado de entrega fabricado. Cenários negativos: cotação vencida,
endereço ausente/alterado, janela incompatível, pedido em preparo/entregue/cancelado, contratação concorrente,
mesma chave com outra intenção, saldo insuficiente/caixa fechado, provedor indisponível e transição inválida.

Prova de produção: `.build/1693-producao/resultado-sanitizado.json` e capturas desktop/celular/rastreio.
Empresa `923f8e8d-406b-4897-8c45-f8fe6f049773`, exclusivamente demonstrativa; vitrine inativa e nenhum envio externo.
O ensaio identificou slug acima do limite ao preparar empresa nova. A correção preserva a UUID em 37 caracteres;
o teste de integração passa a começar sem loja nem vitrine, cobrindo criação e repetição sem duplicação.

### 7.6 Pendências explícitas

- Aceite humano de Thatiane e teste de uma atendente real; a banca simulada não os substitui.
- Credenciais, habilitação comercial/regional e homologação externa de cada prestador.
- Contratação externa com intenção durável e conciliação após timeout/rollback. Ligar produção exige esse
  tratamento, não apenas inserir uma chave na configuração.
- Webhooks externos autenticados, rastreio compartilhável real e envio de mensagens/avaliações por WhatsApp.
- Cancelamento e devolução demonstrativos alteram o ciclo interno. O fluxo operacional ainda precisa
  acionar cancelamento/retorno do prestador pela porta adequada e conciliar a resposta externa.
- Pagamento efetivo do prestador via integração e tratamento de cobranças/ressarcimentos externos.
- Roteirização com trânsito, previsão por parada e restrições de carga/alimento; hoje há heurística e contratos simulados.
- Inclusão de pedido em corrida contratada depende da capacidade específica do prestador; neste laboratório fica bloqueada.
- Atendimento de IA que opera sozinho e conversa com cliente está fora da prova deste corte; a regra automática é configurável e limitada.
- Integração 99 automática sem documentação/acesso comercial confirmado; transporte combinado por fora pode ser registrado manualmente.

### 7.7 Pesquisa rastreável e contratos oficiais

Pesquisa realizada no sprint; documentação comercial/regional deve ser reconferida na sessão que ligar
credenciais. Código de exemplo e fóruns ajudam a encontrar padrões, mas não autorizam capacidades que
o contrato oficial não oferece.

| Serviço | Fonte e implicação para a experiência |
|---|---|
| Uber Direct | [Início](https://developer.uber.com/docs/deliveries/get-started), [FAQ](https://developer.uber.com/docs/deliveries/faq), [janelas](https://developer.uber.com/docs/deliveries/guides/delivery-window), [simulador oficial](https://developer.uber.com/docs/deliveries/guides/robocourier) e [webhooks](https://developer.uber.com/docs/deliveries/guides/webhooks). Criação documentada tem um destino; batching não prova inclusão de parada por esta API. Cotação tem validade; janelas e `tracking_url` devem ser conservados. Retorno produz identificação própria `ret_`, não mera mudança local do pedido original. |
| Lalamove | [API oficial](https://developers.lalamove.com/). Até 16 paradas incluindo coleta; `scheduleAt` em UTC, opção `isRouteOptimized` e `shareLink`. Edição documentada tem condições: uma vez, após alocação do motorista e antes da coleta. Wallet/onboarding e capacidades regionais precisam de homologação. Não foi assumido endpoint genérico de devolução. |
| 99 Entrega | [Portal oficial de integração](https://99entrega.99app.com/b2c-sme/). Cadastro comercial, boleto aprovado e homologação sandbox fazem parte do acesso. O contrato fechado não foi obtido neste sprint; não há endpoint inventado. Registro combinado manualmente permanece alternativa. |

Exemplos oficiais usados como referências de padrões: [Uber Direct SDK samples](https://github.com/uber/uber-direct-sdk-samples)
e [Lalamove API examples](https://github.com/lalamove/api-examples). Fóruns consultados como relatos de experiência,
sem valor de contrato: [discussão r/brdev](https://www.reddit.com/r/brdev/comments/w41sy7) e
[discussão sobre integração 99](https://www.reddit.com/r/brdev/comments/1nopjmd/integra%C3%A7%C3%A3o_com_o_99_food/).

A exceção de simuladores no tenant dedicado foi reconciliada na
[ADR-0061, D2.1](../../adr/0061-conexoes-de-integracao.md#d21--exceção-explícita-laboratório-de-logística-1693).
