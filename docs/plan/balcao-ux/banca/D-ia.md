# Banca UX do Balcão: parte D, auditoria da IA

Base: worktree `ux-balcao-leitura` (master c59b696e5). Somente leitura. Nada foi executado, nenhum servidor subiu.
Convenção: **FATO** = visto no código (arquivo:linha). **SUSPEITA** = inferência minha, a confirmar. Caminhos relativos à raiz do worktree.

## Veredito

A IA existe em 4 pontos do Balcão, com 3 nomes e 2 ícones iguais, e está subaproveitada justamente onde mais ajudaria (resumo da conversa, próxima ação, pedido a partir da conversa). O motor é bom e seguro por desenho (agente nasce desligado, assistente só propõe, texto livre passa por guarda). O que dá "cara de beta" é a casca: rótulos diferentes para a mesma coisa, cartões do "Assistente" que parecem IA mas são regras fixas, dois botões que sugerem resposta, e nenhum registro visível do que o agente fez além da própria mensagem.

---

## 1. Inventário

### 1.1 Provedor e modelo (FATO)

| Item | Valor | Onde |
|---|---|---|
| Cliente único para agente, assistente, resumo, dica de cardápio | API Messages (formato Anthropic), sem retry, sem `cache_control`, sem streaming | `EasyStock.Infra.Integrations/Ia/AnthropicMessagesClient.cs` (grep `cache_control` vazio) |
| Modelo padrão | `claude-sonnet-5`, `MaxTokensAgente` 4096, timeout 60 s | `Infra.Integrations/Ia/AnthropicAgenteOptions.cs:25-30` |
| Provedor trocável | `BaseUrl`, `ApiKeyAgente`, `AutenticacaoBearer` apontam para Fireworks | `AnthropicAgenteOptions.cs:15-23`; homologação usa `accounts/fireworks/models/deepseek-v4p1-flash` em `scripts/homologacao/compose.homologacao.yml:15-20` |
| O modelo de produção real | Não está no repositório (vem de variável de ambiente) | SUSPEITA: Fireworks/DeepSeek, pelo ADR-0058 e CHANGELOG:647 |
| Liga/desliga global | `Anthropic:Enabled`: `false` em `appsettings.json:78`, `true` em `appsettings.Production.json` (chave vem do ambiente), `false` nos composes | `docker-compose.yml:35`, `docker-compose.local.yml:99` |
| Liga/desliga só do agente | `AgenteAtendimentoEnabled` padrão `true` | `AnthropicAgenteOptions.cs:11` |
| Outros provedores | `GeradorAutoPreenchimentoClaude` (Haiku 4.5, `:21`) e `...OpenAI` (gpt-4o-mini) só para descrição de produto/rotulagem, fora do Balcão | `Infra.Postgre/Services/` |
| Cota/limite de gasto | Só grava consumo em `UsoIa` (mês, gerações, tokens). Não achei nenhuma leitura que bloqueie por cota | `AgenteAtendimentoService.cs:215-237`; grep `UsoIa` na Application/Api |

Custo por chamada (SUSPEITA, ordem de grandeza, preço depende do modelo real):
- Turno do agente: system ~1,6 mil tokens (`PromptAtendimento.cs`) + dossiê + caderno + 20 mensagens + ~18 esquemas de ferramenta, repetido a cada iteração (até 6). Estimo 15 a 30 mil tokens de entrada por turno, sem cache.
- Complemento do composer: 20 mensagens + 5 ferramentas de proposta, ~2 a 3 mil tokens de entrada por chamada.
- Resumo do cliente: ~500 a 1 mil tokens, cacheado 12 h.

### 1.2 Capacidades

| # | Capacidade | O que faz | Quem dispara | Onde na tela | Padrão | Back (arquivo:linha) | Front (arquivo:linha) |
|---|---|---|---|---|---|---|---|
| A | **Agente automático** | Responde o cliente no WhatsApp sozinho, com 18 ferramentas; escala para a dona quando não deve responder | Webhook enfileira o turno quando chega mensagem (conversa em `Automatica`) | Aparece só como mensagens com selo "automática" e como a barra de modo | **DESLIGADO**: `Ativo` nasce `false` (decisão #1889, 20/10) | `AgenteAtendimentoService.cs:30-184`; `ConfiguracaoAtendimento.cs:57`; prompt `PromptAtendimento.cs:17-65`; job `ProcessarTurnoAgenteUseCase.cs:12-31` | `Balao.jsx:116`, `BarraModo.jsx:221`, toggle em `features/gestao/atendimento/AbaAtendimento.jsx:260-275` |
| B | **"Sugerir resposta"** | Gera 1 a 3 frases para a atendente revisar; só ferramentas de consulta (4); nada é gravado nem enviado | Clique no botão | Cartão "Sugestão de resposta" dentro do balão "Assistente" | Sob demanda (precisa LLM ligado) | `SugerirRespostaAgenteUseCase.cs:28-100`, rota `POST conversas/{id}/sugestao` em `AtendimentoConversasController.cs:343` | `features/agente/PainelAgente.jsx:22-187`, montado em `app/App.jsx:180` |
| C | **Assistente da dona (chat)** | Pergunta livre com contexto das últimas 20 mensagens; devolve texto + **propostas** de ação (cardápio, nota interna, rascunho, abrir tela, "IA assumir") | Atendente digita no balão | Balão flutuante "Assistente" | Sob demanda | `AssistenteDonaUseCase.cs:138-215`, `PropostasAssistenteDona.cs:25-46`, rota `POST api/atendimento/assistente` | `features/assistente/BalaoAssistente.jsx`, `AcoesPropostas.jsx:15-70` |
| D | **Completar texto (Tab)** | Termina a frase que a atendente está escrevendo (mensagem ao cliente, ou observação de cozinha) | Automático: campo focado, 16 a 1000 caracteres, pausa de 1,2 s, no máximo 1 chamada a cada 8 s, cache 60 s | Linha "Sugestão de IA" abaixo do composer, aceita com **Tab**, Esc dispensa | **LIGADO sempre que o LLM está ligado** (só exige `fonteApi && podeEscrever`) | Reusa a rota C (`perguntarAssistente`) | `aplicacao/useComplementoDeTexto.js:10-89`, `dominio/complementoDeTexto.js:1-31`, `componentes/SugestaoDeTexto.jsx`, `Composer.jsx:104-108`; também na comanda `ficha-cliente/ComandaDoCliente.jsx:129` |
| E | **Resumo do cliente** | 1 a 2 frases (quem é, o que costuma pedir, alertas das notas); sem compra devolve texto fixo | Automático ao abrir o cartão do cliente | Cartão do cliente na ficha (Resumo do Balcão) | Automático; cache 12 h | `ResumirClienteUseCase.cs:20-60`, rota `GET conversas/{id}/resumo-cliente` (`AtendimentoConversasController.cs:109`) | `aplicacao/useResumoDoCliente.js`, `ficha-cliente/CartaoDoCliente.jsx:15` |
| F | **Transcrição de áudio** | Áudio do cliente vira texto para o agente e a atendente | Automático ao chegar áudio | Texto do áudio no balão | Ligado por padrão (`Enabled=true`), faster-whisper próprio na VPS (não é LLM) | `Infra.Integrations/Ia/TranscricaoAudioOptions.cs:11-18`; ADR-0058 emenda | balões de áudio |
| G | Dica de cardápio / pareamento de imagem | Dica por prato e sugestão de foto para prato | Botão "Dica da IA" na gestão | Gestão › Cardápio e Acervo (fora do Balcão) | Sob demanda; cai em dica estática sem IA | `AssistenteDoCardapioUseCase.cs:24-50`, `AcervoController.cs:112-116` | `gestao/cardapio/AbaCardapioApi.jsx:40-43`, `gestao/acervo/SugestoesDoAcervo.jsx` |
| H | Receita com IA | Pesquisa no Wikilivros e organiza insumos | Botão "Pesquisar receita com IA" | Produção (congelada, fora do Balcão) | Sob demanda | `Producao/SugerirReceitaUseCase`, `AtendimentoProducaoPesquisaController.cs` | `gestao/producao/PesquisaProducao.jsx:37` |
| I | **Cartões locais "inteligentes"** (NÃO são IA) | Perfil do cliente, tom da conversa, dica de CDC, padrão de compra, revisão ortográfica, por regras e palavras-chave no navegador | Automático | Dentro do balão "Assistente" | Sempre ligados | n/a | `dominio/assistente.js:31,80,131,197,259`; `Cartoes.jsx`; `BalaoAssistente.jsx:128-133` |
| J | **Classificador local de intenção** (NÃO é IA) | Regex que decide "Precisa de você" na lista (restrição, ameaça, cancelar, desconto, saldo, área) | Automático | Cartões do Balcão e som de aviso | Sempre ligado | n/a | `dominio/agente.js` (519 linhas), usado por `dominio/automatico.js:22,372` |

### 1.3 Ferramentas do agente automático (FATO, `EasyStock.Application/Services/Atendimento/Ferramentas/`)

18 classes. Por efeito:

| Efeito | Ferramentas |
|---|---|
| Leitura (7) | `consultar_cardapio`, `consultar_caderno`, `consultar_pedido`, `consultar_entrega`, `listar_endereco_salvo`, `listar_janelas`, `validar_endereco` |
| Grava dado interno (7) | `registrar_nota` (autor `agente`, `RegistrarNotaFerramenta.cs:12`), `registrar_restricao`, `registrar_interesse`, `registrar_avaliacao`, `avisar_gestao`, `confirmar_endereco`, `confirmar_encomenda` |
| **Sai para o cliente ou mexe em dinheiro/estado (4)** | `enviar_cardapio_imagem` (manda mensagem direto, `EnviarCardapioImagemFerramenta.cs:30-57`), `criar_pedido` (cria pedido + link de pagamento válido 30 min, `CriarPedidoFerramenta.cs:30-35`), `encerrar_conversa` (envia despedida fixa e fecha), `escalar_para_dona` (muda situação) |

O "Sugerir resposta" usa só 4 delas (`SugerirRespostaAgenteUseCase.cs:40-43`). O assistente da dona usa 5 ferramentas de **proposta** que não executam nada (`PropostasAssistenteDona.cs:15-19`).

### 1.4 Guardas do agente (FATO)

- Só responde se `Situacao == Automatica`, dentro da janela de 24 h e com entrada pendente (`AgenteAtendimentoService.cs:60-65`).
- Sem config ativa, sem LLM, ou canal sem agente: escala para a dona com motivo (`:74-84`).
- Cliente bloqueado: escala (`:95-100`).
- Se a dona assumir durante a chamada ao LLM, a resposta é descartada (`:134-142`).
- Texto livre passa por `GuardaRespostaAgente.Avaliar` (`GuardaRespostaAgente.cs:14-60`): barra promessa de encaminhamento sem acionar equipe, afirmação de pedido/pagamento sem ferramenta, frete grátis/desconto, valor em R$ e link sem origem em ferramenta. Se barrar, manda "Vou encaminhar sua mensagem para a equipe..." e escala (`AgenteAtendimentoService.cs:156-171`).
- Máximo de 6 iterações (`:45`); esgotou, escala.
- Regras de negócio duras no prompt: alergia nunca garantida, dinheiro/cancelamento/ameaça sempre para a dona (`PromptAtendimento.cs:47,55`).

---

## 2. Como a IA aparece na tela

### 2.1 Vocabulário atual (FATO)

| Termo na tela | Onde | O que designa |
|---|---|---|
| **Assistente** (ícone `agente`) | Gatilho e título do balão `BalaoAssistente.jsx:258,273`; autor da resposta `:359` | Chat C + cartões I + o cartão B dentro |
| **Agente de atendimento** (aria) / **Sugestão de resposta** (título) (ícone `agente`) | `PainelAgente.jsx:68,70` | B |
| **Sugestão de IA** + `Tab` | `SugestaoDeTexto.jsx:6-7` | D |
| **Automático**, "Automático ligado", "Atendimento automático" | `BarraModo.jsx`, `automatico.js:70-75`, `AbaAtendimento.jsx:267` | A |
| **IA no atendimento** / botão **"IA assumir atendimento"** | `acoesDoAssistente.js:57` | Ação proposta pelo assistente para ligar A na conversa |
| **agente do EasyStok** (title do selo) / "Agente" | `Balao.jsx:116`, `ModalHistorico.jsx:189` | A |
| **automática** (selo com raio) | `Balao.jsx:112-120` | Qualquer mensagem automática, de IA ou do sistema, com o **mesmo selo** |
| **"IA"**, **"Dica da IA"**, "Pesquisar receita com IA" | Gestão | G, H |
| Estados de modo: Automático, Com você, Passou para você, Encerrada, Bloqueado, "Pausado porque você assumiu/escreveu", "Outro atendente" | `resumoDoModo.js:12-17`, `automatico.js:70-120` | Quem responde |

Ou seja: a mesma IA se chama Agente, Automático, Automática, Atendimento automático e "IA"; a ajudante se chama Assistente, Sugestão de resposta e Sugestão de IA. Duas ou três ideias (quem responde sozinho, quem ajuda a atendente, quem completa o texto) com 8 rótulos e o mesmo ícone `agente` (`Icone.jsx:17`) em B, C e no gatilho.

### 2.2 Como a atendente sabe o que a IA fez

FATO:
- Mensagem do agente: selo "automática" com raio. **Não distingue** IA de mensagem do sistema (saudação, aviso de status); só o `title` muda (`Balao.jsx:112-117`). Clicar abre a definição em "Respostas", que para texto de IA não faz sentido (SUSPEITA).
- Passou para a dona: pílula "Passou para você" + motivo curto (`passagem.js:53`, `motivoDeEscalada.js`). Boa.
- Nota/restrição gravada pelo agente: aparece na ficha com autor `agente` (`RegistrarNotaFerramenta.cs:12`).
- **Não existe linha do tempo de ferramentas** ("consultou o cardápio", "criou pedido #123", "mandou a comanda", "avisou a gestão"). `ChamadaFerramenta`/log de uso não existe no domínio (grep vazio). O que ficou do turno: mensagens enviadas, nota, pedido, escalada. Tokens e latência só em log do servidor.
- Aviso `AvisoPedidoNaConversa` etc. cobrem parte (pedido criado entra na conversa) mas é do sistema, não da IA.

### 2.3 Como desfaz e como confirma

| Situação | Confirma? | Desfaz? |
|---|---|---|
| Agente envia mensagem / comanda / cria pedido | **Não** (autonomia total quando A está ligado) | Não há "desfazer". Só **Assumir** (para as próximas) ou cancelar o pedido na mão. SUSPEITA: mensagem já enviada não volta |
| "Devolver ao automático" | Sim, modal explicando efeito (`BarraModo.jsx:279-297`) | Assumir de novo |
| Assistente propõe ação | Sim, um botão por ação, nada roda antes do clique; só "Enviar ao cliente" é primário (`AcoesPropostas.jsx:44-62`) | Nota salva e cardápio enviado não têm desfazer |
| Sugerir / Tab | Texto vai ao campo, a atendente envia | Esc dispensa a sugestão; ctrl-z do campo |

### 2.4 Onde confunde (FATO salvo marcação)

1. **Dois botões para a mesma coisa**: "Sugerir resposta" (B, `POST /sugestao`) e perguntar "sugira uma resposta" no chat (C, ferramenta `propor_rascunho`, placeholder em `BalaoAssistente.jsx` "Ex.: sugira uma resposta ou prepare uma nota"). Dois endpoints, dois prompts, dois estilos de resultado.
2. **B mora dentro do balão "Assistente"** mas se chama "Agente de atendimento" por aria e "Sugestão de resposta" no título. O mesmo "agente" é o robô que responde sozinho. Em `InstrucaoSugestao` o prompt diz "a dona assumiu esta conversa" (`SugerirRespostaAgenteUseCase.cs:46`) mas o botão aparece também com a conversa no automático (SUSPEITA, não conferi se o botão some).
3. **Cartões de regra parecem IA**: "Tom da conversa" (palavra-chave, `assistente.js:131`), "Perfil do cliente" e "Padrão de compra" (cálculo local), "Revisão ortográfica", dica de CDC. Estão no mesmo balão e com o mesmo estilo do chat de IA. A atendente não tem como saber o que é modelo e o que é regra.
4. **Cartão de foto fabricado**: `analisarFotoSuspeita` gera "confiança" 60-89% a partir da soma dos códigos do texto (`assistente.js:227-239`) e mostra "Exemplo de análise de foto... Valor simulado". Está gateado por `!fonteApi` (`BalaoAssistente.jsx:131`), então **não aparece em produção** (modo API). Mas ainda é código de demonstração dentro da feature.
5. **"IA assumir atendimento"** (`acoesDoAssistente.js:57`) quando o resto da tela diz "Devolver ao automático" (`BarraModo.jsx:272`): mesma ação, dois nomes e dois lugares.
6. **Complemento por Tab é invisível até acontecer**: sem sinal de que existe; a linha "Sugestão de IA" surge sob o texto depois de ~1,2 s e some. Conflita com Tab de navegação para quem usa teclado (mitigado: só aceita com cursor no fim, `useComplementoDeTexto.js:112-115`).
7. **Dois classificadores do "precisa de humano"**: o servidor (prompt + guarda + escalada) e o navegador (regex em `dominio/agente.js:179-199`, `automatico.js:372`). Podem divergir: o cartão do Balcão sobe "Precisa de você" por regex mesmo que o agente tenha respondido (SUSPEITA de divergência; a regra é duplicada por desenho, comentário `agente.js:1-10`).
8. **Peso morto do protótipo**: `dominio/agente.js` (519 linhas), `modosDeConexao.js` ("Simulado / Claude pela linha de comando / Claude pela API"), "Bancada de teste" (`PainelAgente.jsx:140`, só em DEV). Não aparece para a atendente, mas mantém o modelo mental de "modo simulado".

---

## 3. Riscos atuais

| # | Risco | Tipo | Evidência |
|---|---|---|---|
| R1 | Com A ligado, a IA **fala com o cliente sem revisão humana**. A única revisão é a guarda por regex depois do texto pronto | FATO | `AgenteAtendimentoService.cs:152-172`. Mitigação real: a guarda barra números/links/promessas sem fonte. Lacuna: tom, erro factual de cardápio que veio de ferramenta correta mas mal interpretada, resposta fora de contexto |
| R2 | **Ações com efeito sem confirmação**: `criar_pedido` (pedido + link de pagamento), `enviar_cardapio_imagem`, `encerrar_conversa`. O controle é só por instrução no prompt ("somente quando o cliente pedir explicitamente", `PromptAtendimento.cs:51`) | FATO que não há confirmação humana; SUSPEITA do quanto o modelo respeita o prompt | `CriarPedidoFerramenta.cs:30-60`. Atenuante: o fluxo preferido é a comanda do cliente, que ele mesmo confirma |
| R3 | **Falha silenciosa do turno**: se o turno lança exceção, `ProcessarTurnoAgenteUseCase` só loga e devolve `Ignorado` (`:26-30`). A conversa fica `Automatica` com a mensagem do cliente sem resposta e **sem escalar** | FATO do código; SUSPEITA do impacto | Mitigação existente: SLA `ClienteSemResposta` (#1427) e "respostaAtrasada" no Balcão. Sem retry no cliente HTTP (`AnthropicMessagesClient.cs`); erro HTTP dentro de `GerarAsync` escala corretamente (`GeracaoAgenteAtendimento.cs:77-83`), mas exceção fora dele não |
| R4 | **Falha silenciosa nos recursos de apoio**: resumo do cliente devolve texto nulo e a tela só mostra os números (`ResumirClienteUseCase.cs:52-57`); o Tab engole qualquer erro (`useComplementoDeTexto.js:57`). Por desenho, mas a atendente nunca sabe se "não tem sugestão" ou "a IA caiu" | FATO | Só o painel B mostra erro ("A conexão com a IA está indisponível", `PainelAgente.jsx:99-105`) |
| R5 | **Injeção de prompt**: texto do cliente e transcrição de áudio entram no contexto de um agente com ferramentas que enviam mensagem, criam pedido e gravam notas | SUSPEITA | Mitigação: guarda de saída, `[interno]` nunca repetido, ferramentas sem acesso a outros clientes (escopo da conversa). Não encontrei teste adversarial |
| R6 | **Complemento por Tab manda contexto da conversa + rascunho parcial ao provedor a cada pausa** (privacidade e custo) sem opção de desligar na tela | FATO do disparo; SUSPEITA do volume | `Composer.jsx:104-108`: ligado por padrão em produção. Não há chave "completar texto" em `AbaAtendimento.jsx` |
| R7 | **Custo sem teto**: `UsoIa` só soma; sem cota, alerta ou painel. Agente sem prompt cache | FATO | grep `UsoIa`; `AnthropicMessagesClient.cs` |
| R8 | **Turno e rascunho podem divergir**: o agente responde enquanto a atendente digita (a guarda #1288 cobre "assumiu durante o turno", mas o composer pode estar com rascunho seu) | SUSPEITA | `AgenteAtendimentoService.cs:134-142` |
| R9 | **"Sugerir resposta" disponível com o agente automático em curso** pode produzir texto que a atendente envia e disputa com o agente | SUSPEITA | `PainelAgente` não bloqueia por modo |
| R10 | Mensagem de encaminhamento "Vou encaminhar sua mensagem para a equipe..." sai ao cliente quando a guarda barra | FATO | `AgenteAtendimentoService.cs:145,168`. É o único texto de IA que a guarda barrou e mesmo assim fala com o cliente (texto fixo, ok) |

Pontos fortes a preservar: agente desligado por padrão; assistente só propõe; `SugerirResposta` read-only; guarda de saída; descarte se a dona assumiu; trava de alergia no prompt e no cliente.

---

## 4. Oportunidades, ordenadas por valor/esforço

Escala: esforço P (menos de 1 dia), M (2 a 4 dias), G (1 semana ou mais). Risco = de errar com o cliente ou de custo.

| # | Oportunidade | O que já existe | O que falta | Esforço | Risco |
|---|---|---|---|---|---|
| 1 | **Resumo da conversa no topo** ("quer 2 bolos para sábado, falta endereço") | `ResumirClienteUseCase` já é um molde pronto (prompt curto, 1 chamada, cache 12 h, texto nulo em falha); `MontarConversacao` e o dossiê existem (`GeracaoAgenteAtendimento.cs:151`); `CartaoDoCliente` já mostra texto de IA | Caso de uso `ResumirConversa` (últimas 20 msgs + pedido em andamento), rota GET, chave de cache pela última mensagem, linha fixa sob o cabeçalho do fio. Prompt pede "o que o cliente quer / o que falta / próximo passo" em 1 frase | **P** | Baixo: somente leitura, nunca sai ao cliente |
| 2 | **Rascunho de resposta já pronto, aceito com Tab** (quando a conversa abre com cliente aguardando, o campo vem com a resposta em cinza) | B (`SugerirRespostaAgenteUseCase`) já gera o texto só com consulta; D (`useComplementoDeTexto`, `SugestaoDeTexto`) já faz o Tab | Chamar B automaticamente quando há mensagem do cliente sem resposta e o campo está vazio; exibir como "fantasma" no composer; fundir B e D na mesma UI. Cache por id da última mensagem | **P** a **M** | Médio: custo (uma chamada por conversa aberta) e erro de conteúdo; mitigado porque a atendente lê e envia. Sempre com selo "IA", nunca envia sozinho |
| 3 | **Próxima ação sugerida** (chips: "Mandar cardápio", "Cobrar", "Pedir endereço", "Passar para cozinha") | `proximoPassoDepoisDeResponder` já existe por regra (`resumoDoModo.js`); ações propostas e executor já existem (`AcoesPropostas.jsx`, `executarAcaoDoAssistente`); rotas de enviar cardápio, nota, rascunho prontas. Cobrança tem estado no front (`dominio/cobranca.js`) | Gerar as propostas **sem a atendente perguntar** (junto do resumo do item 1, em uma só chamada). Novas propostas: `propor_cobranca` (usa fluxo existente de cobrança), `propor_pedir_endereco` (rascunho). Mostrar 1 ou 2 chips sob o resumo | **M** | Baixo se continuar "proposta + clique"; risco só de ruído, então limitar a 2 |
| 4 | **Extrair o pedido da conversa para o carrinho, com confirmação** | `consultar_cardapio` (ids reais), `criar_pedido`, comanda editável (`aplicacao/api/comanda.js`: `adicionarItem`), padrão de proposta com clique, `captura.js` (versão por regex, só no simulado) | Nova proposta `propor_itens_no_pedido` (lista de `cardapio_item_id` + qtd + data), validada contra o cardápio no servidor (descarta id inexistente, como já faz `Traduzir`), card "Adicionar 2x Bolo de cenoura, 1x Brigadeiro à comanda?" com botão; ao clicar chama `adicionarItem` existente. Atendente confere, a IA não cobra | **M** | Médio: erro de quantidade/data. Mitigado por confirmação e por mostrar o texto do cliente ao lado |
| 5 | **Unificar entrada do assistente num atalho "/"** no composer (hoje "/" abre Respostas prontas) | `useAtalhoBarra`, `SeletorRespostas` (`Composer.jsx`), chat C | Itens "IA: resumir", "IA: sugerir resposta", "IA: pedir endereço" no mesmo seletor. O chat livre fica como "Perguntar..." | **M** | Baixo; depende do item 2 para valer a pena |
| 6 | **Registro do que o agente fez** (linha "IA consultou o cardápio, enviou a comanda, anotou X") | `ContextoTurnoAgente` já recebe os resultados das ferramentas (`ResultadosDasFerramentas`, `ContextoTurnoAgente.cs`); `Mensagem` do sistema e `[interno]` já existem | Gravar uma nota interna curta por turno com ferramenta de efeito (`criar_pedido`, `enviar_cardapio`, `registrar_*`, `avisar_gestao`), mostrada discreta no fio ("IA: criou pedido #123"). Ajuda auditoria e desfaz | **M** | Baixo; mexe em `AgenteAtendimentoService` (tier alto por ser serviço central: cuidado com a regra de processo) |
| 7 | **Busca em linguagem natural** ("quem pediu bolo sem lactose em setembro") | Dossiê, histórico, notas, `consultar_caderno`, restrições registradas | Hoje é busca por texto; precisaria de ferramenta de consulta cross-cliente (nova) ou tradução para filtros existentes. Sem índice semântico no repo | **G** | Médio-alto: acesso a dados de vários clientes, LGPD, custo. **Não priorizar agora** |
| 8 | **Nenhum botão de "tudo certo, a IA cuida"** (modo copiloto vs piloto): hoje A é tudo ou nada por loja (`Ativo`) | `SituacaoConversa.Automatica` por conversa já existe; assumir/devolver por conversa já existem | Um terceiro estado "Copiloto": A gera a resposta mas **guarda como rascunho para aprovar com 1 clique** em vez de enviar. Reaproveita B. Resolve R1 e R2 e é o passo natural antes de ligar o automático no dono | **M** a **G** | Médio: muda o fluxo central do agente; recomendado só após item 2 provar valor |
| 9 | Limpar o balão: remover cartões locais que simulam IA, esconder "Exemplo de análise de foto" do código, separar "Dicas do sistema" de "IA" | Cartões I | Rotular "Dica" vs "IA", ou remover os de pouco valor (tom por palavra, ortografia) | **P** | Nenhum |
| 10 | Teto e visibilidade de custo (UsoIa) | `UsoIa` já grava mês/tokens | Mostrar no Gestão, alerta em 80% e corte do complemento (D) antes do agente (A) | **P** a **M** | Baixo |

Ordem recomendada: 1, 2, 9 (rápidos e visíveis) → 3, 4 (valor de tempo da atendente) → 6, 10 (segurança/custo) → 5, 8. Deixar 7 de fora.

Premissa de custo: itens 1, 2 e 3 geram chamadas automáticas por conversa aberta. Usar cache por (conversa, última mensagem), uma chamada que devolve resumo + próxima ação + rascunho juntos (saída estruturada por ferramenta, como já faz `AssistenteDoCardapioUseCase` com `SchemaDica`) e modelo barato (a produção já usa provedor trocável).

---

## 5. Modelo mental único recomendado

**Um nome: "Assistente". Um ícone: o robozinho `agente` atual. Um lugar: o balão do Assistente + um selo "IA" nos pontos onde a IA escreve ou sugere.**

Regras:
1. **Dois papéis, dois verbos, um nome.** O Assistente *sugere* (atendente decide) e *atende sozinho* (modo automático). Quando atende sozinho, o selo na mensagem diz "Assistente" (hoje "automática"); o modo se chama **"Assistente atendendo"** vs **"Você atendendo"**. Termos "Agente", "Automático", "IA assumir atendimento", "Atendimento automático" saem.
2. **Selo "IA" só em texto que um modelo escreveu.** Mensagens do sistema (saudação, aviso de status) mantêm "automática". Cartões de regra (tom, CDC, ortografia) passam a se chamar "Dica" e perdem o ícone do robô.
3. **Um lugar para pedir.** "Sugerir resposta" (B) deixa de ser botão e vira o rascunho fantasma do composer (oportunidade 2). O chat livre fica no balão para perguntas e ações.
4. **Toda ação com efeito tem o mesmo ritmo:** o Assistente propõe, mostra o que vai acontecer, a atendente toca uma vez. Exceção explícita e visível: modo "Assistente atendendo" liga ao nível da conversa, com aviso na barra de modo e no cartão do Balcão.
5. **Falha dita em voz alta.** Se a IA falhou ou está desligada, uma linha discreta diz isso em vez de calar.

Justificativa:
- O produto já fez a escolha, parcialmente: o gatilho e o título do balão são "Assistente" e a ação proposta já mexe com a IA como "propor, a atendente confirma". É o termo que menos custa generalizar.
- "Agente" é jargão técnico (`PainelAgente`, `dominio/agente.js`) que a Thatiane não usa; "Automático" descreve o *modo de atendimento*, não a IA, e deve virar estado do Assistente, não outro personagem.
- Um nome único reduz de 8 para 2 os rótulos (Assistente / Você) e casa com a decisão do dono ("IA a favor da ferramenta, não exagerada"): a IA vira ajudante discreta (resumo, rascunho, chips), sem cartões que competem por atenção.
- Riscos R1 e R2 baixam sem custo de UI se o modo copiloto (oportunidade 8) vier antes de ligar o automático no dono.

---

## 6. O que não verifiquei

- O modelo e o provedor de **produção** (variáveis de ambiente fora do repositório).
- Se o botão "Sugerir resposta" fica desabilitado com a conversa em automático.
- Se há testes adversariais de prompt injection.
- Custo real em R$: sem preço do provedor e sem token medido de produção, só ordem de grandeza.
- Nenhuma tela foi aberta; o que digo sobre a aparência vem do código.
