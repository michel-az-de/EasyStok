# Homologação orgânica do atendimento: aderência e spec de correção (H01 a H16)

Issue: #1318 · Base medida: `origin/master` `544510f9` · Data: 2026-10-01 · Versão 2, depois da banca revisora · Relacionadas: [10-console.md](10-console.md), [11-console-fechamento.md](11-console-fechamento.md)

## Veredito

**Aderência geral: __GERAL__%** (107 cenários aplicáveis, 3 não testáveis nesta bancada). **Por causa-raiz, sem contar o mesmo defeito várias vezes, fica perto de 67 %** (estimativa da banca: 35 falhas vêm de 24 causas). Os dois números dizem a mesma coisa: o agente conversa bem e a segurança de entrada está sólida; o que derruba a nota é a **cola entre o console e o servidor**.

**Go-live do atendimento: NÃO pronto.** Bloqueiam **todos os Altos (A-01 a A-14)**; uma só lista, sem exceção. Três deles fazem o sistema **mentir ao cliente**: A-02 (diz "estamos abertos" com a loja fechada à mão), A-08 (diz "levo o caso para a Baba" e não escala) e A-06 (cliente volta e o agente nega o histórico e oferece venda).

## Como foi medido

| Item | Valor |
|---|---|
| Bancada | API do `origin/master` em contêiner, Worker, Postgres, console `VITE_FONTE_DADOS=api`, tudo local e isolado |
| Clientes | 46 conversas simuladas por webhook assinado da Meta (texto, botão, imagem, áudio, figurinha, localização) e pelo chat do site |
| Agente | `deepseek-v4p1-flash` pela Fireworks; 118 chamadas medidas por proxy: 209 mil tokens de entrada, 37,5 mil de saída, 234 mil lidos de cache |
| Meta e Mercado Pago | **Falsos e locais** (`scripts/homologacao/`): gravam cada envio, devolvem entregue e lido, aprovam pagamento e estornam. **Nenhuma mensagem real saiu.** O falso devolve sempre sucesso, então cenários de sucesso podem estar otimistas |
| Operação humana | Console no navegador: assumir, responder, devolver, encerrar, comanda, cadastro, zona de frete, bloqueio, estorno |
| Critério | **Passou** = comportamento observado igual ao esperado, sem estado errado; **parcial** vale 0,5; **não testável** sai da conta. Amostra única de um modelo estocástico (n = 1); quem definiu o esperado também classificou |

Limites: sem Meta e Mercado Pago reais (conectores sem autorização), Instagram, Messenger, e-mail, SMS, impressora, Lalamove, Web Push. Os scripts da bateria entram neste PR em `scripts/homologacao/` para reproduzir.

## Resultado por área

__RESUMO__
O que **funciona** (provado): assinatura e repetição do webhook, isolamento por empresa, permissão (operador recebe 403 no bloqueio), injeção de prompt, alergia e celíaca, escalada com motivo legível, rajada e inundação sem custo em cascata, estorno automático do pagamento duplicado com aviso, link vencido reemitido, avaliação 30 min depois da entrega com botão, validação de mensagem programada, encerramento que persiste, cadastro pela conversa (F18), imagem do cliente (com a Meta falsa servindo a mídia).

## Achados

Severidade: **Alta** = trava a operação, engana o cliente ou deixa o cliente sem resposta; **Média** = a dona decide errado ou perde tempo; **Baixa** = acabamento. "Já no plano" aponta a issue que cobre o tema.

### Altos

| ID | Achado | Prova | Causa | Já no plano |
|---|---|---|---|---|
| A-01 | **Incluir pedido em viagem existente devolve 409 sempre.** Despacho do entregador próprio não funciona | `POST viagens/{id}/paradas` → `CONCURRENCY_CONFLICT` 3 de 3; `DbUpdateConcurrencyException` no log | `ParadaViagem.cs:30` faz `Id = Guid.NewGuid()`; `EntregasConfiguration.cs:43` não tem `ValueGeneratedNever`; a viagem é carregada com `Include(Paradas)`; o EF marca a parada nova como alterada e o `UPDATE` afeta 0 linhas. Mesmo defeito do `ClienteTag` (#1312) | Não |
| A-02 | **O agente não sabe que a loja está fechada.** Com "Fechar loja agora" ele diz "estamos abertos e recebendo pedidos"; as automáticas de loja fechada vêm desligadas e, quando ligadas, saem depois do agente com `{abre}` literal. O checkout já recusa em `ForcarFechada`, mas a conversa não | Servidor `estaAberta=false`; cliente recebeu saudação, "Sim, estamos abertos" e "Voltamos {abre}" | Nenhuma referência a expediente em `Services/Atendimento`; `GET automacoes` devolve 6 regras `ligada=false` por ausência de linha; `ModeloTextoAtendimento.cs:8` só conhece `nome`, `pedido`, `faixa` | #1240 (parcial) |
| A-03 | **O console mostra regras, textos e respostas que o servidor não tem.** A dona edita "Primeira resposta" e acha que mudou o que sai; o texto real vem de `GET atendimento/configuracao` (`saudacaoPrimeiroContato`), sem tela | Console: 6 regras "ligadas" e 8 respostas prontas; API: 6 `ligada=false`, `respostas-prontas=[]`; texto enviado ≠ prévia | Biblioteca e regras da massa ainda alimentam a tela; saudação real sem editor | #1240 |
| A-04 | **Pedido nasce sem janela visível.** A vaga é reservada, mas `agendadoParaEm` fica nulo: a comanda diz "Janela não escolhida" e o resumo ao cliente traz só a data | `GET pedidos/{id}` `agendadoParaEm=null`; `vaga_ocupada` preenchida | `AgendadoParaEm` nunca é atribuído em `CheckoutCoreService.cs:182-190` (atende agente, checkout logado e guest). O desenho vigente usa a **vaga como fonte da janela** (`KdsPedidoQueries.cs:34-40`, `CalculadoraInicioPrevistoPedido.cs:8`) | Não |
| A-05 | **Cozinha e Entregas só mostram "hoje".** Pedido pago hoje para amanhã não aparece; não há seletor de data | `GET kds/pedidos` vazio; com `?data=2026-10-02` vem o pedido; `kdsApi.js:6` não manda `data` | Console chama o KDS sem data | #1242 (parcial) |
| A-06 | **Cliente volta depois de Encerrar e o agente esquece tudo.** Reclamação e reposição prometida somem; resposta "não encontrei pedido" e oferta de venda | Fabio: conversa nova após encerrar | Contexto do agente vem só da conversa aberta | #1319 cobre o caderno da loja, não a memória do cliente |
| A-07 | **Falha do modelo deixa o cliente no vácuo.** `max_tokens` ou erro vira escalada com motivo técnico, sem aviso ao cliente. Botão tocado depois do pedido fechado reexecuta o agente | Julia: 3 mensagens sem resposta; motivo à dona "stop_reason=max_tokens" | Sem retry, sem teto maior para modelo que raciocina, sem mensagem padrão | Não |
| A-08 | **Foto de reclamação não escala.** Agente diz "levo o caso para a Baba" e a conversa segue "Automático ligado" | Hugo Foto: situação 1, sem motivo | Mídia sem texto não aciona escalada | Não |
| A-09 | **Ações que a dona precisa avisam "ainda não ligado":** marcar pago à mão, conferir comprovante, estorno, bloqueio, nota do cliente, ocorrência, resumo e avaliação do fechamento, e as demais de `naoLigadas.js` (reabrir, trocar janela, refazer cobrança, endereço do pedido, tags, recebido na entrega). O modal de bloqueio ainda colhe motivo antes de recusar | 8 cliques, 8 avisos; as rotas existem (`/clientes/{id}/bloquear`, `/ocorrencias`, `/pedidos/{id}/pagamentos`) | F09 e F14 não entraram | #1239, #1244, #1255 |
| A-10 | **A dona só é avisada com a tela aberta, e ninguém avisa que o background parou.** Escalada e pagamento sem baixa só chegam por Web Push, que falha sem chaves VAPID e não tem plano B. Sem o Worker, outbox, avisos e automáticas ficam parados em silêncio | `notif_logs_envio`: "WebPush:PublicKey/PrivateKey nao configurados"; 7 escaladas `Pendente`; outbox `Status=1` sem Worker. O `docker-compose.azure.yml` não traz VAPID | Único canal da dona é push; roteiro de go-live não exige Worker nem VAPID | #1310 |
| A-11 | **A fila de turnos do agente vive em memória na API.** Deploy ou reinício perde turnos de clientes; o comentário "perda aceitável" contradiz A-07 | `BackgroundQueueService` e loop de 500 ms em `AtendimentoFilaTurnoAgenteBackgroundService` | Fila não durável, sem drenagem no desligamento | Não |
| A-12 | **Falha de envio ao cliente é invisível.** Erro cru em inglês ("Message undeliverable"), sem sinal "Precisa de você", sem retry nem modelo fora da janela de 24 h. O cliente fica sem a mensagem e a dona não sabe | Conversas Num Invalido e Fora Janela: status Falhou, lista sem sinal | Falha só grava `Erro` | Não |
| A-13 | **Chat do site não tem agente nem mensagem de ausência.** A conversa já nasce assumida; o visitante fica no vácuo até a dona responder, no canal de entrada da casa | `conversa.Assumir(agora)` em `ChatSiteUseCases.cs`; visitante sem resposta em 2 min | Desenho do S36 | **Decisão do Felipe** (abaixo) |
| A-14 | **Sem alerta quando um cliente bloqueado ameaça.** Mensagem de bloqueado fica quieta (certo para custo), mas "vou processar vocês" não gera aviso à dona | Kleber após bloqueio: conversa `Situacao=2`, sem lembrete novo | Bloqueio desliga o agente e o alerta juntos | Não |

### Médios

| ID | Achado | Correção curta |
|---|---|---|
| M-12 | Console calcula "Fora da área de entrega" com prefixos da massa e contradiz a zona da API (CEP 05422-001, dentro da zona, aparece fora). Quando o agente recusa por área, a dona não é avisada e "liberar por exceção" não aparece | Console usa `GET storefront/{slug}/frete`; sinal "fora de área" na lista |
| M-13 | "Primeira resposta" sai para reclamação e xingamento e soma saudação dupla com o agente | Sem saudação se a 1ª frase é reclamação; uma saudação só |
| M-14 | Console mostra expediente "Fechado o dia todo" nos 7 dias; o servidor tem 08h às 22h. "Fora do horário" existe em 2 telas com textos diferentes. `{abre}` fica literal | Ler o expediente real; uma fonte do texto; criar a variável `abre` e rejeitar chave desconhecida ao salvar |
| M-15 | Nota interna de escalada aparece como balão enviado e como "Você:" na lista, e fica "enviando" | Tipo "nota interna", sem status de envio, fora do preview |
| M-17 | Números errados: "0 pedidos" e "Histórico 0" com pedido entregue; "Já era cliente" com 0 pedidos; "18 conversas deste cadastro" no bloqueio | Ligar ao dossiê (S25); contar só do cadastro |
| M-18 | Cobrança: console diz Pix, rotula "link de cartão" e o cliente lê "Pix ou cartão". Resumo ao cliente sem a faixa da janela | Honrar `meioAnterior`; incluir a faixa |
| M-19 | Avisos de esteira saem a cada 2 min: "saiu" e "entregue" chegam juntos | Achar a chave do loop do Worker (clamp de 60 s) |
| M-20 | No modo API a galeria lista pratos que a casa não vende (Lasanha clássica R$ 35, Tortéi de costela) e Fidelidade mostra cupons da massa | Esconder o que não tem backend |
| M-22 | Agente: descreve a marca como "comida caseira, feita na hora"; responde em português a quem escreve em inglês; aceita 60 e 200 unidades sem checar capacidade | Caderno da marca (S54); regra de idioma; teto de quantidade que escala |
| M-23 | Agente atende um cliente por vez: com 16 clientes juntos a 1ª resposta foi de 3 s a 135 s (≈ 4 s por cliente). Para cozinha pequena é raro; vira Alta só se o volume real exigir | Concorrência 4, ver H07b |

### Baixos

Seletor de janela enviado quando o cliente já disse o período; banner "Você respondeu" logo após Assumir; botão tocado aparece como balão vazio; endereço mostra o bairro no lugar da cidade; aviso antigo fica na tela; lembrete de pagamento sem baixa sem nome nem valor; lembrete "Responder" para cliente já bloqueado; áudio não avisa a dona.

## Spec de correção

Cada fatia segue as [convenções do executor](README.md#convenções-do-executor-vinculantes-resumo-do-claudemd-v40): issue, worktree, Red primeiro, gate, PR. **Aceite é sempre teste automatizado nomeado**; onde o comportamento depende do modelo, usar modelo falso determinístico (latência e falha fixas) e, para a conversa real, suíte de 5 rodadas com taxa mínima de 4 de 5.

| Fatia | Cobre | Correção | Red (falha antes) | Aceite | Tier | Depende |
|---|---|---|---|---|---|---|
| H00 | todos | Versionar os scripts da bateria em `scripts/homologacao/` (simuladores, Meta e MP falsos, proxy, matriz) e a suíte de regressão em CI | n/a | Bateria roda de ponta a ponta com um comando e gera a matriz | baixo | nenhuma |
| H01 | A-01 | `ValueGeneratedNever` em `ParadaViagem.Id` e migration só de snapshot, como no #1312 | Teste de **repositório com Postgres**: incluir parada em viagem carregada → `DbUpdateConcurrencyException` (o teste do use case com NSubstitute não reproduz) | Incluir, reordenar, sair e entregar pelo console e pela API; F04 validado em `ViagensDespachoTests` | **alto** (migration) | nenhuma |
| H02 | A-02, M-13, M-14 | (a) Expediente (aberta, fechada, próxima abertura) entra no contexto do agente; (b) com `estaAberta=false` a automática de loja fechada **substitui** o turno do agente, sem corrida; (c) variável `abre` criada a partir de `ExpedienteLoja` e chave desconhecida rejeitada ao salvar a regra; (d) regras com **default em código**, sem migration de seed nem ligar envio em tenant existente sem consentimento | Teste: tenant com `ForcarFechada` e mensagem do cliente → hoje o agente responde "abertos" | Teste de integração: loja fechada → nenhuma resposta do agente, uma automática, nenhum `{...}` no texto (varredura sobre todas as chaves do modelo) | baixo | nenhuma |
| H03 | A-03, M-20, M-14 | Console lê `configuracao`, `automacoes`, `respostas-prontas` e `expediente`; editor da saudação real; esconde massa no modo API | E2E Playwright: prévia da "Primeira resposta" igual ao texto gravado em `atendimento_mensagens`; regra desligada no servidor aparece desligada | Nenhuma regra "ligada" na tela com `ligada=false`; lista de respostas = API | baixo | funde com #1240 |
| H04 | A-04, M-18 | **Fonte única = vaga.** Comanda e resumo ao cliente leem a faixa da `VagaOcupada` (como `ObterPedidoConversaUseCase.cs:76`); não gravar segunda fonte. Forma de pagamento escolhida segue na cobrança | Teste dos 3 chamadores (agente, checkout logado, guest): pedido com vaga → comanda mostra "Janela não escolhida" | Comanda mostra "sex 02/10 · Tarde"; resumo traz a faixa; Pix escolhido aparece como Pix na cobrança e no texto | baixo | nenhuma |
| H05 | A-05 | Seletor de data (hoje, amanhã, próximos) em Cozinha e Entregas; padrão = hoje; contador "para amanhã" | E2E: pedido pago para amanhã, tela em "hoje" → vazio | Pedido de amanhã visível ao trocar a data, sem mudar o dia de produção | baixo | funde com #1242 |
| H06 | A-06 | Resumo das últimas conversas, ocorrências e promessas **do mesmo cliente e da mesma empresa** entra no contexto do agente, reaproveitando `ResumoDossieParaAgente` e `MarcadorInterno` (#1292); conversa nova de cliente com ocorrência aberta nasce marcada para a dona; prazo de retenção e consentimento da memória definidos | Testes de isolamento: dois tenants com o mesmo telefone, telefone reciclado, texto de reclamação com instrução embutida (injeção persistente) | Agente cita a reclamação anterior e escala; **nunca** traz dado de outro cliente nem obedece instrução vinda de memória | baixo | H13, #1319 |
| H07a | A-07, A-08 | Teto de tokens adequado ao modelo que raciocina; 1 retry com recuo; mensagem padrão ao cliente na falha ("já avisei a Baba"); mídia sem texto em conversa com pedido recente escala com motivo; botão de pedido já fechado responde sem reexecutar o agente | Modelo falso que falha e que estoura tokens; imagem sem legenda | Falha do modelo nunca deixa o cliente sem resposta; imagem escala (testes por cenário) | baixo | H13 |
| H07b | A-11, M-23 | Fila de turnos **durável** (tabela ou outbox) com drenagem no desligamento; concorrência global 4 com **uma conversa por vez** (para não quebrar a rajada); recuo em 429; **disjuntor de custo** por empresa e por cliente por dia | Reiniciar a API no meio de 10 turnos → hoje perde | Nenhum turno perdido no reinício; 16 clientes com modelo falso de 4 s: 1ª resposta p90 ≤ 20 s; rajada continua com uma resposta | baixo | H13 |
| H07c | M-22 | Regra de idioma; teto de quantidade que escala; caderno da marca (S54) | Cliente em inglês; pedido de 200 unidades | Resposta no idioma do cliente; quantidade acima do teto escala | baixo | #1319 |
| H08 | A-09, M-17 | Ligar ao backend existente: marcar pago, comprovante, estorno e ocorrência, bloqueio e desbloqueio, nota, dossiê, resumo e avaliação do fechamento, e **todas** as ações de `naoLigadas.js`. Teste de autorização por papel e trilha de auditoria em estorno e bloqueio | `prova-f06` percorre `naoLigadas.js`: nenhuma ação sem ligar nem avisar | As ações persistem e aparecem na API após recarregar; operador recebe 403 em estorno e bloqueio; contadores vêm do dossiê | baixo (policy já existe) | #1239, #1244, #1255 |
| H09 | A-10, A-12, A-14 | Alerta da dona por **canal sem dependência da Meta** (e-mail) além de push e WhatsApp com modelo; **conteúdo mínimo** (identificador e link, sem nome, telefone nem texto da reclamação); destinatário validado; VAPID e Worker obrigatórios no compose do go-live; falha de envio traduzida, com sinal "Precisa de você" e reenvio por modelo; alerta de ameaça de cliente bloqueado | Escalada sem push configurado → só `Pendente` | Escalada chega à dona em ≤ 1 min por e-mail (teste com SMTP falso); falha de envio aparece em português e na fila "Precisa de você" | baixo | #1310, H13 |
| H10 | M-12, M-15 | Console usa o frete da API para "fora da área"; sinal "fora de área, liberar?" na lista; nota interna como tipo próprio, sem status de envio | E2E: CEP dentro da zona aparece dentro | Nenhum rótulo de área contradiz `GET frete` | baixo | nenhuma |
| H11 | M-19 | Achar no Worker a chave que gera o intervalo de 2 min (há clamp de 60 s no loop de agendamento) e baixar para 15 a 30 s | Teste com relógio falso (#1307): avisos de preparo, saiu e entregue em lote | Cada aviso sai ≤ 30 s depois do evento | baixo | nenhuma |
| H12 | A-13 | Conforme decisão do Felipe | n/a | n/a | n/a | decisão |
| H13 | A-10, A-11 | **Observabilidade antes de H07 e H09**: métricas de profundidade da fila, p90 da 1ª resposta, 429, escaladas pendentes por idade, idade do outbox e batimento do Worker; alerta "Worker parado" | n/a | Painel e alerta disparam em teste (Worker derrubado em 2 min) | baixo | nenhuma |
| H14 | todos | **Rollout por flag por empresa** (agente e automáticas), ordem canário → medir → ligar, kill switch por empresa; rollback sem depender de `git revert` | n/a | Desligar a flag para uma empresa para o agente em ≤ 1 min | baixo | H13 |
| H15 | A-04 (colateral) | Revalidar `CalculadoraInicioPrevistoPedido` com vaga e sem vaga depois da H04 | Teste de unidade existente + 3 chamadores | Sem regressão no início previsto | baixo | H04 |
| H16 | gate | Smoke contra Meta e Mercado Pago **reais em sandbox** (envio, janela de 24 h, modelo, assinatura, link, Pix, estorno) | n/a | Roteiro verde, executado pelo Felipe | n/a | Felipe |

**Ordem:** H00, H13 → H01, H02, H04, H07a, H07b, H09 (em paralelo) → H03, H08, H05, H06, H10, H11, H14 → H15, H16. **Gate de saída do atendimento (substitui "≥ 90 %"):** os 14 Altos fechados, cada um com teste automatizado em CI; bateria reexecutada com ≥ 90 % por causa-raiz e **zero Alta aberta**; H16 verde.

## Custo medido e estimado

Medido: 118 chamadas, 209 mil tokens de entrada, 37,5 mil de saída, 234 mil lidos de cache em 46 conversas, ou **4,5 mil de entrada, 815 de saída e 2,6 chamadas por conversa**. A conta em dinheiro é **estimativa da banca com preços assumidos, não medidos**: cerca de US$ 0,003 por conversa no modelo da Fireworks e US$ 0,03 no Sonnet; mil conversas por mês custam US$ 3 ou US$ 27. Use ×3 a ×4 como margem (amostra curta; H06 aumenta a entrada e o retry da H07a dobra o custo nas falhas, por isso o disjuntor da H07b).

## Decisão que só o Felipe toma

**A-13, chat do site sem agente.** Opções: (A) ligar o agente e a ausência no chat do site como no WhatsApp; (B) manter só humano e mostrar "respondemos em até X min" ao visitante; (C) manter como está. Recomendação: **A**, porque o site é o canal de entrada da Casa da Baba. Se A, o teste de identidade do chat anônimo é obrigatório: telefone digitado não pode vincular a outro cliente.

## O que falta provar fora desta bancada

| Item | Para fechar | Quem |
|---|---|---|
| Meta real (envio, entrega, lido, janela de 24 h, modelos) e Mercado Pago real | Autorizar os conectores do WhatsApp Business e do Mercado Pago em claude.ai; número e usuários de teste (**H16**) | Felipe |
| Instagram e Messenger | Página e conta vinculadas, token de página | Felipe |
| Web Push e impressora | Chaves VAPID; modelo da impressora | Felipe |
| Cancelamento da 2ª cobrança vencida | Relógio falso (#1307) | agente |

Backlog de cenários que a banca pediu e **não foram executados**: corrida entre a dona assumindo e o agente respondendo, duplo clique gerando dois pedidos, vaga concorrente, pagamento recusado e parcial, descadastro ("parar") e pedido de exclusão (LGPD), reinício do Worker no meio do processamento, injeção por nome do contato, legenda de imagem e nota, pedido de outro cliente por UUID, dois tenants com o mesmo telefone, 1.000 clientes com 1 mensagem (teto de custo), mensagem de 100 mil caracteres, varredura de log por token e telefone, retenção de mídia.

## Parecer da banca revisora

Cinco revisores, cada um com uma lente, leram a versão 1. Todos pediram mudanças; nenhum reprovou os achados, e todos foram incorporados nesta versão.

| Lente | Veredito sobre a v1 | O que mudou na v2 |
|---|---|---|
| Produto e operação da dona | Aprova com ressalvas | Lista única de Altos; M-16 virou A-12; M-21 virou A-13; A-11 (fila) rebaixada para M-23; as 3 mentiras nomeadas; EN03, BL05 e IN06 ganharam achado e fatia; `naoLigadas.js` inteiro entrou na H08 |
| Arquitetura e backend | Parcialmente certa | A-01 confirmada e **H01 passou a ALTO** (migration de snapshot, precedente #1312); A-04 reescrita (vaga é a fonte; H04 sem segunda fonte); A-02 corrigida (checkout já recusa; `{abre}` é variável não criada, não vazamento); H02 sem migration de seed |
| Segurança e LGPD | Não aprovada | H06 com Red de isolamento e injeção persistente; H09 com conteúdo mínimo e canal sem Meta; disjuntor de custo (H07b); backlog adversarial listado |
| QA e testabilidade | Aritmética certa, método frágil | Nota por causa-raiz; aceites determinísticos; H07 dividida; scripts versionados (H00); gate de saída trocado; smoke real (H16) |
| Operação, custo e go-live | Não aprovada | H13 (observabilidade) antes de H07 e H09; H14 (flag por empresa); fila durável (A-11); concorrência 4 por conversa; conta de custo |

Pendências que a banca deixou para a execução: preços reais de modelo para fechar a conta de custo; limite de requisições por minuto do provedor em produção; base legal e contrato de operador de dados para e-mail e Meta (H09); retenção da memória e da mídia.

## Matriz completa de cenários

__MATRIZ__

## Rollback

Documento e scripts apenas. Cada fatia é um PR isolado; H01 tem migration só de snapshot, reversível; H02 não migra dados.
