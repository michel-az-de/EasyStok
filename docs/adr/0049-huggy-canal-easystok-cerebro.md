# ADR-0049 — Huggy como canal de atendimento, EasyStok como cérebro

- Status: Proposto (vira Aceito no merge da issue #1040)
- Data: 2026-09-22
- Contexto: levantamento com a Tatiana em 21/09/2026 (docs 01-03) e análise do backend em 22/09 (doc 04), ambos em `C:\Users\felip\Downloads\`
- Relacionados: ADR-0048 (plataforma única, sem SaaS), ADR-0042 (esteira canônica), ADR-0030 (outbox transacional), ADR-0010 (RLS), decisão de 08/08/2026 de manter notificações internas (sem Hiram)

## Contexto

O levantamento pede um sistema centrado em atendimento conversacional por WhatsApp com
interpretação de linguagem livre (D3), possibilidade de a dona assumir a qualquer momento (D4),
uma tela só para todos os canais (D5) e automação da burocracia sem automação do cuidado (D2).

Medido em 22/09 no repositório: não existe nada de inbound de WhatsApp, conversa, inbox ou agente.
Existem o provider de saída da Meta Cloud API, o SSE de operação, o cardápio v2, o checkout com
janelas de entrega, Pix via Efi com webhook e estorno, e o módulo de notificações com outbox.

O front do operador será reescrito em outra tecnologia (fora deste ADR). O Felipe decidiu publicar
o atendimento na Huggy e não usar o Hiram.

## Decisão

1. **Huggy é o canal e a inbox humana.** WhatsApp, Instagram e widget do site entram pela Huggy.
   A dona atende na Huggy (app e web). Respostas rápidas, notificação em todos os dispositivos e
   histórico da conversa são recursos nativos da Huggy, não do EasyStok.
2. **EasyStok é o cérebro.** Recebe os eventos da Huggy por webhook, roda o agente (LLM com
   ferramentas sobre a própria API), cria pedido antes do pagamento, gera cobrança Pix, opera a
   esteira, guarda o cadastro, as notas, as tags e o histórico, e dispara avisos e campanhas.
3. **Toda saída de WhatsApp passa pela Huggy.** O número vive na Huggy. O módulo de notificações
   ganha o provider `whatsapp:huggy`; os providers Meta Cloud e Twilio de WhatsApp saem.
4. **Assumir a conversa é estado do chat na Huggy.** A Huggy só dispara `receivedMessage` para chat
   em fila ou automático. Quando a dona entra (`agentEntered`, `sentAllMessage`), o agente cala.
   Quando ela encerra (`closedChat`), a próxima mensagem abre chat novo em automático. Isso cumpre
   RN-04 sem relógio (D4). Avisos transacionais continuam saindo pelo outbox (RN-05).
5. **Sem Hiram.** Notificações ficam em `EasyStock.Infra.Notifications` e
   `EasyStock.Application/Services/Notifications`, como decidido em 08/08/2026.
6. **Módulo por tenant.** Atendimento só aparece para empresa com a flag `ModuloAtendimento`
   (ADR-0048): a FMA não vê conversa nem agente.

## Alternativas consideradas

- **Inbox nativa no EasyStok** (recomendação do doc 04). Descartada pelo Felipe: a Huggy entrega
  inbox, multicanal, app da dona e onboarding do WhatsApp Business (a Huggy é BSP da Meta), e o
  esforço vai para o agente e para a operação, não para UI de chat.
- **Chatwoot self-hosted.** Descartado: mais um sistema para operar sozinho.
- **Bot de delivery pronto.** Descartado: é o atendimento por menu numérico que a entrevista
  rejeitou (áudio 04, D3).

## Consequências

- A "tela única" (D5) vira duas por natureza: Huggy para conversar, console para operar. O elo é
  o dossiê do cliente sincronizado em campos do contato na Huggy e o link para a ficha no console.
- Mensagem ativa (aviso de status sem chat aberto, campanha, avaliação) depende de recurso da Huggy
  a confirmar em spike: chat ativo por contato e template HSM. Fora da janela de 24 h a Meta exige
  template aprovado; o custo de conversa de marketing é maior que o de utilidade.
- O agente precisa de chave Anthropic e de um cliente HTTP em `Infra.Integrations`. O padrão de
  chamada já existe em `EasyStock.Infra.Postgre/Services/GeradorAutoPreenchimentoClaude.cs`.
- O webhook da Huggy não tem assinatura. A defesa é segredo no caminho da URL, handshake por
  token, idempotência por evento em `WebhookRecebido` e rate limit.
- Se um dia o atendimento sair da Huggy, o que muda é `IHuggyClient` e o webhook; conversa,
  mensagem, agente, pedido e avisos ficam.
