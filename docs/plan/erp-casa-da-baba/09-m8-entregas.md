# M8 Entregas · janelas, áreas, entregadores, viagens, rota e chamados

Issue: #1316 · Decisão: [ADR-0056](../../adr/0056-erp-da-casa-da-baba-front-unico.md) item 3 (módulo novo,
não estava no rascunho) · Data: 2026-10-01 · Base medida: master `b713263a`.
Plano irmão (dono de S01–S53 e F01–F18): [10-console.md](../atendimento-whatsapp/10-console.md),
[11-console-fechamento.md](../atendimento-whatsapp/11-console-fechamento.md). Fundação:
[01-fundacao.md](01-fundacao.md) (M0.2 rotas `#/m/entregas/painel` = `#/entregas` e
`#/m/entregas/minhas-viagens`; M0.3 perfil Entregador; D-05 sessão de dispositivo). Este documento não
reescreve S nem F.

## 1. Veredito

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

**Problema.** O backend reserva vaga em qualquer data dos próximos 60 dias, mas a tela não deixa escolher o
dia: a comanda mostra as 10 primeiras janelas com vaga e nenhuma tela mostra o que está marcado para a
semana. Encomenda de sábado feita na terça vira conversa e caderno.
**Abordagem.**
- Comanda (`SeletorJanelaApi.jsx`): seletor de dia (hoje, amanhã, calendário até o limite de D8-02) que manda `DataInicio`/`DataFim`, já aceitos por `ListarJanelasAtendimentoInput` (`ListarJanelasAtendimentoUseCase.cs:8-13`); a lista mostra as janelas daquele dia.
- Agenda `#/m/entregas/agenda`: `GET api/atendimento/agenda?de&ate` (Operador), leitura nova que agrega `VagaOcupada` ativa por data e janela (ocupadas de capacidade) e lista os pedidos de cada janela; bloqueios (S45) aparecem como dia fechado.
- O agente (`listar_janelas`) já usa o mesmo use case: só ganha o parâmetro de data, sem regra nova.
- Pagamento da encomenda segue D8-03; com a opção A não muda nada (RN-23: pedido antes, cobrança na hora, link reemitido pelo job da S11).
**Aceite.**
- [ ] Pedido criado na conversa para daqui a 10 dias ocupa a vaga daquele dia e aparece na agenda.
- [ ] Dia bloqueado não oferece janela; data além do limite volta 400 com o motivo.
- [ ] A agenda e a oferta contam a mesma vaga (uma regra só, ADR-0014).
- [ ] Encomenda de amanhã aparece em "Amanhã" na cozinha (M4.6) e gera o lembrete da véspera (M3.8).
**Testes (Red).** `AgendaEntregasQueryTests.ContaVagasPorJanelaEData`, `...BloqueioFechaDia`; `prova-m8-agenda.mjs`.
**Fora.** Encomenda com sinal ou pagamento parcial (D8-03 B); recorrência ("toda sexta").
**Depende de.** M8.1. **Tamanho.** M · **Tier.** baixo (sem migração) · **Rollback.** revert; a comanda volta às 10 primeiras.

### M8.3 · Endereço de entrega no próprio pedido

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
- [ ] Mudar o endereço do cliente não muda o endereço de pedido já criado (teste de integração).
- [ ] Pedido criado pela conversa grava lat/lng quando o Google responde; sem chave, grava o texto e avisa.
- [ ] Trocar o endereço para fora da área põe o pedido em aprovação.
- [ ] Isolamento por empresa no `PATCH`.
**Testes (Red).** `PedidoEnderecoEntregaTests.SnapshotNaCriacao`, `...TrocaForaDaAreaPedeAprovacao`; `KdsPedidoQueriesPostgresTests.EnderecoDoPedidoVence`.
**Depende de.** nada; é pré-requisito da F17. **Tamanho.** M · **Tier.** alto (migration) · **Rollback.** migration `Down`; leitura volta ao cadastro.

### M8.4 · Minhas viagens: a tela do entregador no celular

**Problema.** O motoboy próprio recebe a viagem por mensagem solta; a dona marca "entregue" por ele no fim do
dia (US-044 quer isso automático). `Entregador` não tem usuário (`Entregador.cs:16-26`) e nenhuma rota lê
"a viagem deste entregador".
**Abordagem (conforme D8-01).**
- Tela de celular, uma coluna, fonte grande: a viagem atual com as paradas na ordem, para cada uma nome, endereço do pedido (M8.3), apto, telefone (ligar e WhatsApp), faixa prometida, observação de entrega, número do dia para conferir na retirada (S53), botões "Abrir no Maps" (`RotaMaps`) e **"Entregue"** (`POST viagens/{id}/paradas/{pedidoId}/entregue`, `AtendimentoEntregasController.cs:150`). Sem preço, sem histórico do cliente (LGPD: só o necessário para entregar).
- Opção A (link): ao "Saiu para entrega", o EasyStok gera um link da viagem com token de uso limitado, no padrão do link do cardápio da conversa (`LinkCardapioConversa`: `TokenHash`, `ExpiraEm`, `Entities/Atendimento/LinkCardapioConversa.cs:16-21`), e a dona envia ao `Entregador.Telefone` pelo WhatsApp com um toque. Rotas anônimas `GET api/entregas/viagem/{token}` e `POST .../paradas/{pedidoId}/entregue`, só da viagem do token, expiram na conclusão da viagem ou em 12 h; rate limit como o da Meta.
- Opção B (login): `Entregador.UsuarioId` (migration), perfil Entregador da M0.3 com porta `#/m/entregas/minhas-viagens`, sessão longa pela D-05; `GET api/atendimento/viagens/minhas`.
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

**D8-01 · Como o entregador entra na tela dele**
- A) (Recomendado) Link por viagem mandado pelo WhatsApp, sem login: o entregador é terceiro (persona, `02-ESTORIAS…md:38`), troca muito, e o padrão de link com token já existe (S48). O perfil Entregador da M0.3 deixa de ser necessário.
- B) Perfil Entregador com login e "Minhas viagens", como no diagrama do README e na matriz da M0.3.
- C) Os dois: link agora, login para quem for fixo da casa.

**D8-02 · Até quantos dias à frente a encomenda pode ser marcada**
- A) (Recomendado) 60 dias, o limite que o código já impõe.
- B) 30 dias.
- C) 14 dias, o padrão atual da oferta.

**D8-03 · Pagamento da encomenda**
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
