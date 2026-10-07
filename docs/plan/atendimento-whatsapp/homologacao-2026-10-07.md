# Homologação de 07/10/2026: o que foi pedido e onde está

Fonte: captura de tela e fala do Felipe de 07/10/2026, 11:29, revisão 1, lida inteira (13:57 de
gravação). Registro: issue #1447. Plano irmão: [ERP da Casa da Baba](../erp-casa-da-baba/README.md).

**Resumo:** 70 itens (63 falados, 7 vistos só na tela): 5 corrigidos e mergeados, 3 aprovações a
manter, 47 com frente aberta nesta rodada ou em outra sessão, 7 só dependem do Felipe, 2 têm frente e
dependência dele ao mesmo tempo, e 6 ainda estão sem frente.

Legenda: ✅ mergeado · 🟡 issue ou PR aberta · ⏸ depende do Felipe · ⬜ sem frente.
Status medido em 07/10 no `gh` (issue ou PR aberta = 🟡; mergeado = ✅). Atualize a linha quando a PR fechar.

## 1. Decisões tomadas na homologação

| Min | Decisão | Onde vale |
|---|---|---|
| 00:07 | Atendimento é um módulo do EasyStok, não um "console de atendimento" à parte | hall de módulos (#1447) |
| 00:22 | EasyStok atende só a Casa da Baba até segunda ordem (tenant único) | já é o ADR-0056 |
| 00:33 | Redesign completo aproveitando a engenharia que existe | todas as frentes |
| 01:37 | Referência externa só inspira; a cara é sempre a da Casa da Baba | design system v1 (M0.1) |
| 06:57 | Tela inicial é um hall com os módulos; cada módulo tem menu próprio | #1447 |
| 09:10 | Cliente não fica sem atendimento dentro do horário | #1429, #1443 |
| 09:16 | Abrir loja exige caixa aberto; fechar loja no horário só dono ou gerente, com justificativa gravada em log | #1443 |
| 13:15 | Prioridade agora: atendimento e entrega. Tela de login fica para depois | ordem desta rodada |

Aprovado por ele (manter como está): sugestão do agente (04:38), claro e escuro (06:46), ordenação
Urgência / Mais recentes / Entrega mais cedo (07:35), "Devolver ao automático" (12:30).

## 2. Pedidos e destino

| # | Min | Área | Pedido | Status | Onde |
|---|---|---|---|---|---|
| 1 | 01:08 | Balcão | Tela inicial do Balcão confusa ("Ninguém esperando você") | 🟡 | #1442 |
| 2 | 01:56 | Balcão | Pouca informação, busca fraca, canais pouco claros; quer simples e minimalista | 🟡 | #1442 |
| 3 | 02:19 | Balcão | Algo pisca sem explicação; cartões de canal confundem | 🟡 | #1442 |
| 4 | 02:29 | Balcão | Abrir já em "Todas", não em "Precisa de você" | 🟡 | #1442 |
| 5 | 02:33 | Balcão | Mostrar a foto do cliente | ⬜ | a conferir: a Cloud API da Meta não entrega foto de perfil do contato (inferência) |
| 6 | 02:41 | Áudio | Transcrição não apareceu; o agente diz que não ouviu | ✅ | #1423 (faster-whisper na VPS); validar em produção |
| 7 | 02:45 | Respostas | Tela de respostas feia, cheia de blocos e botões | 🟡 | #1441 |
| 8 | 02:57 | Compositor | Anexo, resposta rápida e nota interna diretos | 🟡 | #1441 |
| 9 | 03:10 | Respostas | Resposta rápida direta no compositor | 🟡 | #1441 |
| 10 | 03:17 | Nota | Nota interna como post-it, ao lado da conversa | 🟡 | #1441 |
| 11 | 03:40 | Nota | Nota dava erro ("ainda não ligado") | ✅ | #1438 |
| 12 | 03:47 | Áudio | Gravar áudio sem saber se enviou, sem carregamento | 🟡 | #1444 |
| 13 | 04:05 | Agente | Automático leva dados e tags do cliente para a ficha | ⬜ | relacionado a #1445 |
| 14 | 04:25 | Ficha | Editar cadastro na própria ficha | 🟡 | #1442 |
| 15 | 04:31 | Ficha | Recolher seções da ficha | 🟡 | #1442 |
| 16 | 04:40 | Agente | Assistente executa ações (mandar cardápio, anotar) | 🟡 | #1445 |
| 17 | 05:02 | Fotos | Foto da galeria falhava com "Failed to fetch" | ✅ | #1439 |
| 18 | 05:24 | Meta | Trocar o número na Meta | ⏸ | §3.1 |
| 19 | 05:46 | Agente | Usar o histórico de conversas para treino ou RAG | ⏸ | §3.4 |
| 20 | 06:16 | Site | "Visitante do site" vira lead sem contato | 🟡 | #1431 |
| 21 | 06:28 | Site | Cardápio do site com geolocalização falhando | 🟡 | #1323 |
| 22 | 06:31 | Entregas | Geolocalização para 99 Entregas, Uber e Lalamove (iFood não) | 🟡 ⏸ | #1304, §3.6 |
| 23 | 06:42 | Entregas | API de geolocalização configurável | 🟡 | #1323, #1304 |
| 24 | 06:57 | Módulos | Hall com cada módulo da documentação | 🟡 | #1447 |
| 25 | 07:22 | Módulos | Cada módulo com menu próprio | 🟡 | #1447 |
| 26 | 07:46 | Loja | Abrir loja é abrir caixa; hoje nada aparece | 🟡 | #1443 |
| 27 | 08:00 | Caixa | Ao abrir, conferir o caixa de ontem, mostrar saldo, retificar à mão | 🟡 | #1443 |
| 28 | 08:08 | Pagamento | Mercado Pago não avançou | ⏸ | §3.2 |
| 29 | 08:16 | Cardápio | Painel do cardápio sem fotos | 🟡 | #1448 |
| 30 | 08:21 | Cardápio | Cardápio pouco organizado | 🟡 | #1448 |
| 31 | 08:26 | Cardápio | Imagens erradas; as fotos devem vir da API | 🟡 ⏸ | #1448, §3.3 |
| 32 | 08:40 | Ficha | Histórico 0 para quem já falou com a casa | ✅ | #1438 (log das automáticas a conferir) |
| 33 | 08:55 | Loja | "Fechar loja agora" não funciona | 🟡 | #1443 |
| 34 | 09:00 | Avisos | Push para celular, e-mail e outros canais | 🟡 | #1428 |
| 35 | 09:10 | SLA | Cliente não fica sem resposta no horário | 🟡 | #1429 |
| 36 | 09:16 | Loja | Fechar loja só com justificativa em log | 🟡 | #1443 |
| 37 | 09:20 | Loja | Só dono ou gerente fecha no horário | 🟡 | #1443 |
| 38 | 09:43 | Janelas | Janela de entrega presa em "Carregando as entregas" | 🟡 | #1440 (erro de `EmpresaId` corrigido na #1435) |
| 39 | 09:53 | Janelas | Criar janela nova (amanhã 12h–14h, 16h) | 🟡 | #1440 |
| 40 | 10:01 | Roteiro | Pedidos por situação e para onde despachar | 🟡 | #1440 |
| 41 | 10:19 | Roteiro | Imprimir o roteiro do dia por janela e por quem busca | 🟡 | #1440 |
| 42 | 10:35 | Entregas | Tela de Entregas confusa e sem funcionar | 🟡 | #1440 |
| 43 | 10:49 | Lembretes | Lembretes não funcionam; push, e-mail e WhatsApp | 🟡 | #1428, #1425 |
| 44 | 10:59 | Gestão | Gestão confusa; abas não são gestão | 🟡 | #1447 |
| 45 | 11:17 | Caixa | Caixa da Gestão sem explicação, sem gravar | 🟡 | #1443 (conteúdo), #1447 (lugar: Financeiro) |
| 46 | 11:26 | Janelas | Cadastro de janela na Gestão não implementado | 🟡 | #1440 (conteúdo), #1447 (lugar: Entregas) |
| 47 | 11:38 | WhatsApp | "Conectar WhatsApp Business" fora da Gestão, em tela própria | 🟡 | #1447 (Configurações › Canais) |
| 48 | 11:56 | Módulos | Mapear os módulos que entram agora | 🟡 | #1447, §4 |
| 49 | 12:05 | Cozinha | Imprimir cupom não fiscal (canhoto) | 🟡 | #1446 |
| 50 | 12:13 | Cozinha | Cozinha não funciona como no protótipo | 🟡 | #1446 |
| 51 | 12:21 | Cozinha | Lançar a comanda como no protótipo | 🟡 | #1446 |
| 52 | 12:30 | Agente | Texto do "Devolver ao automático" mais objetivo | 🟡 | #1445 |
| 53 | 12:43 | Cozinha | KDS abre em janela separada e vazio | 🟡 | #1446 (dados), #1447 (abre pelo hall na mesma aba) |
| 54 | 12:52 | Cozinha | Movimento e animação na fila, "mais jogo que sistema" | ⬜ | ideia, junto da #1446 |
| 55 | 13:15 | Login | Ajustar a tela de login | ⏸ | adiado pelo Felipe, §3.5 |
| 56 | 13:21 | Automáticas | Respostas automáticas mais bem mapeadas | 🟡 | #1441 |
| 57 | 13:36 | Site | Chat do site aceitar foto, arquivo e áudio | ⬜ | sem frente |
| 58 | 13:43 | Tags | Tag do cliente não funciona | 🟡 | #1441 |
| 59 | 00:39 | ERP | Gestão de toda a operação (cozinha, entrega, pagar, receber) | 🟡 | plano [erp-casa-da-baba](../erp-casa-da-baba/README.md) |
| 60 | 01:25 | Login | Gostou do efeito de login de uma referência | ⏸ | §3.5 |
| 61 | 04:38 | Agente | (aprovado) sugestão do agente | ✅ | manter |
| 62 | 07:35 | Balcão | (aprovado) ordenação das conversas | ✅ | manter |
| 63 | 12:30 | Agente | (aprovado) "Devolver ao automático" funciona | ✅ | manter |

Vistos na tela, sem fala:

| # | Min | Área | Achado | Status | Onde |
|---|---|---|---|---|---|
| 64 | 04:10 | Ficha | "Cliente desde" sem data | ⬜ | sem frente |
| 65 | 05:02 | Balcão | "3 mensagens não entregues" fixo no topo | ⬜ | a conferir com o painel da S58–S60 |
| 66 | 08:54 | Automáticas | Todo dia "Fechado o dia todo" e loja "Forçada aberta na mão" | 🟡 | #1443 (a conferir) |
| 67 | 10:03 | Entregas | "EmpresaId é obrigatório" ao carregar entregas | ✅ | #1435 |
| 68 | 11:59 | WhatsApp | Coexistência sem AppId, Embedded Signup e segredo | ⏸ | §3.1 |
| 69 | 11:59 | Integrações | Lalamove e 99 sem credencial | ⏸ | §3.6 |
| 70 | todas | Sessão | Rodapé "Nome · null" (empresa nula na faixa) | 🟡 | #1447 |

## 3. O que depende do Felipe

### 3.1 Trocar o número do WhatsApp na Meta

Checklist de referência. Os passos da Meta mudam: conferir na documentação dela no dia (inferência,
não medido nesta rodada). Base técnica do nosso lado: [whatsapp-coexistencia](../../dev/whatsapp-coexistencia.md).

Decisão antes de tudo:
- [ ] Qual número fica: o oficial da loja ou o de teste de hoje.
- [ ] **Coexistência** (o número continua no app WhatsApp Business do celular e também fala pelo
      EasyStok, #1417) ou **só API** (o número sai do app).

Do lado da Meta (Felipe):
- [ ] Portfólio da empresa verificado e nome de exibição aprovado para o número novo.
- [ ] App da Meta com AppId, configuração do Embedded Signup e segredo do app; esses valores vão para o
      `.env` da VPS pelo script, nunca pelo chat (hoje faltam, visto na tela às 11:59).
- [ ] Coexistência: app WhatsApp Business atualizado no celular, número ativo e com uso.
- [ ] Só API: verificação em duas etapas do número desligada (ou PIN conhecido) e o número liberado da
      conta anterior. Se estiver preso a outra conta ou provedor, abrir chamado no suporte da Meta.
- [ ] Modelos de mensagem: são por conta (WABA). Mesma conta, continuam; conta nova, recriar e
      aprovar de novo os modelos que o EasyStok usa (retomada da S58, notificações da N6).
- [ ] Limite de envio e qualidade começam do zero no número novo.
- [ ] Avisar os clientes pelo número antigo (mensagem fixa e status) antes de desligá-lo.

Do nosso lado (agente, com o Felipe rodando o deploy):
- [ ] Conectar pelo console em Configurações › Canais (coexistência grava o número sozinha) ou
      vincular o `phone_number_id` novo à loja (`PUT api/admin/tenants/{id}/whatsapp`, #1102).
- [ ] Token de usuário do sistema com permissão de mensagens e gestão na conta nova (`EZ_META_*`).
- [ ] App inscrito na conta (`subscribed_apps`) e webhook com a URL e o token de verificação.
- [ ] Tela Canais mostrando "WhatsApp ligado", webhook verificado e última mensagem recebida.
- [ ] Smoke: receber e responder texto, foto e áudio; mandar modelo fora das 24 h.

### 3.2 Mercado Pago

Estado e passos completos em [08-mercado-pago.md §0](08-mercado-pago.md). O código está pronto e
nunca rodou contra o Mercado Pago real. Só o Felipe destrava:
- [ ] Entrega em produção: endereço da cozinha (coordenada), faixas de frete e janelas (hoje 0 zonas e
      0 janelas, todo CEP sem cobertura).
- [ ] Conta da Casa da Baba com credencial de produção; `MercadoPago__*` no `.env` da VPS; webhook com
      segredo.
- [ ] Sandbox antes, em janela curta (memória de 02/10: host sslip.io para o retorno).
- [ ] Tirar a manutenção do site quando os dois itens acima estiverem prontos.

### 3.3 Fotos com o host antigo (sslip.io)

A #1437 contornou o envio pelo console lendo a foto pela chave do storage, mas as URLs das fotos
continuam gravadas com o host antigo (inferência a medir no banco).
- [ ] Felipe: definir o host público definitivo e trocar `FileStorage__PublicBaseUrl` (e
      `FileStorage__S3__PublicBaseUrl`, se for S3) no `.env` da VPS.
- [ ] Agente: medir quantas URLs têm o host antigo (contagem, sem dado pessoal) e propor um script
      idempotente que reescreve só o prefixo, com backup antes. Rodar só com GO do Felipe.
- [ ] Conferir cardápio do site, painel do cardápio e galeria do atendimento depois da troca.

### 3.4 Treino e RAG com o histórico de conversas (proposta curta)

- **Fontes:** respostas da dona nas conversas do EasyStok, pedidos fechados, cardápio, respostas
  prontas e o caderno da loja ([14-caderno-da-loja.md](14-caderno-da-loja.md), que já prevê busca
  semântica na S56).
- **Privacidade (LGPD):** usar só o que serve ao atendimento; tirar nome, telefone, endereço e
  documento antes de indexar (troca por marcador); nada sai do servidor da loja além da chamada ao
  modelo, com provedor sem retenção; prazo de retenção definido; cliente que pedir exclusão sai também
  do índice. Sem treinar modelo próprio (fine-tune): o dado não vira peso de modelo.
- **Abordagem:** (1) medir volume e qualidade das conversas; (2) extrair pares pergunta → resposta da
  dona, anonimizados e marcados por intenção; (3) índice vetorial no Postgres que já existe; o agente
  recebe 3 a 5 exemplos parecidos junto do caderno; (4) medir pela taxa de sugestão usada sem edição
  (o botão "Usar" do painel do agente já existe).
- **Decisão do Felipe:** aprovar fontes, prazo de retenção e se o aviso de privacidade do site e do
  WhatsApp precisa citar o uso.

### 3.5 Tela de login

Adiada pelo Felipe (13:15). Quando voltar: login de um passo (spec M0.2), marca da Casa da Baba e
conta Google (#1325) mantida.

### 3.6 99, Uber e Lalamove

- [ ] Felipe: contas e credenciais (Lalamove e 99 Entregas sem credencial, visto às 11:59; Uber a avaliar).
- [ ] Agente: a #1304 grava e testa as chaves por loja; a #1323 traz a geolocalização configurável.
      iFood fica fora (D3-02 e fala das 06:31).

### 3.7 Quem fecha a loja

Regra decidida: dono ou gerente, com justificativa e log (#1443). Falta o Felipe dizer se mais alguém
da equipe pode, quando os perfis por módulo (M0.3) entrarem.

## 4. Módulos que entram agora

| Nº | Módulo | No console hoje (#1447) | Próximo passo |
|---|---|---|---|
| 1 | Cardápio | em breve | M1 (#1448 cuida das fotos no painel do atendimento) |
| 2 | Produção | em breve | M2 |
| 3 | Atendimento | Balcão; Horários e mensagens | #1441, #1442, #1444, #1445 |
| 4 | Cozinha | Fila de preparo; Produção e cardápio do dia | #1446 |
| 5 | Financeiro | Caixa do dia | #1443; contas a pagar e receber seguem no EasyStock.Web |
| 6 | Campanhas | Fidelidade e cupons (ainda não ligado) | F15 |
| 7 | Configurações | Canais (WhatsApp); Integrações de entrega | #1304; usuários e perfis na M0.3 |
| 8 | Entregas | Painel de entregas; Janelas de entrega | #1440, #1323 |

Prioridade dita pelo Felipe: atendimento e entrega primeiro, depois cozinha. Permissão por perfil e
módulo (M0.3) ainda não existe: o hall mostra todos os módulos para quem entra, e quem nega é a API.
