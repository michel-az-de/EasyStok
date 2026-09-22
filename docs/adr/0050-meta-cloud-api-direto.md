# ADR-0050 — WhatsApp direto na Meta Cloud API (supersede ADR-0049)

- Status: Aceito
- Data: 2026-09-22
- Supersede: ADR-0049 (Huggy como canal). Mantém tudo o mais do ADR-0049: EasyStok é o cérebro, sem Hiram, front do operador em outra tecnologia, módulo gated por tenant.
- Relacionados: ADR-0048, ADR-0042, ADR-0030, ADR-0010; issue #1042

## Contexto

O ADR-0049 escolheu a Huggy como canal e inbox humana. Ao escrever as specs contra a API v3 da
Huggy (22/09) apareceram quatro pontos que pesam contra o caso da Casa da Baba:

1. **Mensagem ativa é o coração do levantamento** (aviso de status, avaliação em dois botões,
   campanha) e na Huggy dependia de recurso a confirmar em spike; na Cloud API é nativo (texto na
   janela de 24 h, template fora dela, botões interativos de resposta).
2. **Duas telas.** A dona conversaria na Huggy e operaria no console. A entrevista pediu uma tela
   só (D5) e o front novo já vai existir de qualquer forma.
3. **Segurança do webhook.** A Huggy não assina; a Meta assina com HMAC-SHA256.
4. **Custo fixo.** Assinatura mensal por atendente para uma operação de uma pessoa, além do
   repasse da Meta.

O que a Huggy entregava pronto era a inbox com app no celular, o Instagram e o widget do site. A
entrevista disse que pedido pelo Instagram é improvável e o site já direciona para o WhatsApp.

## Decisão

1. **O WhatsApp da Casa da Baba entra direto na Meta Cloud API.** Webhook próprio, cliente Graph
   próprio, número registrado na Cloud API.
2. **A inbox é uma tela do console novo** (lista de conversas, thread, mídia, respostas rápidas,
   assumir e devolver ao automático), consumindo a API do EasyStok. Notificação da dona fora do
   console vai por Web Push, que já existe.
3. **Assumir a conversa é estado do EasyStok** (`Conversa.Situacao`): qualquer mensagem enviada pela
   dona pelo console suspende o automático daquela conversa; a volta é ação explícita dela (D4,
   RN-04). Avisos transacionais continuam pelo outbox (RN-05).
4. **Toda saída de WhatsApp usa o provider da Meta já existente** no módulo de notificações,
   com regra de janela de 24 h: texto livre dentro, template aprovado fora.
5. **Instagram e widget ficam fora de escopo** até haver demanda medida. O site continua
   direcionando para o WhatsApp.
6. **Sem BSP intermediário** (Huggy, Twilio, Zenvia). Os providers Twilio e Zenvia saem na poda.

## Alternativas consideradas

- **Huggy** (ADR-0049). Substituída pelos quatro pontos do contexto.
- **Twilio como BSP.** Intermediário sem ganho: a Cloud API já é acessível direto e o provider
  da Meta já existe no repositório.
- **Chatwoot self-hosted.** Mais um sistema para operar sozinho; a inbox no console custa menos
  que operá-lo.

## Consequências

- O front novo precisa da tela de conversa. O backend expõe conversas, mensagens, envio com
  mídia, assumir, liberar e encerrar.
- Mídia recebida é baixada da Meta (URL temporária, 5 minutos) e guardada no storage S3
  compatível que já existe; o console lê pela API com autenticação.
- A verificação da empresa na Meta (bloqueio #6 do repositório casa-da-baba, aberto desde maio)
  passa a ser responsabilidade direta do Felipe, sem BSP conduzindo.
- O número sai do aplicativo WhatsApp Business do celular ao entrar na Cloud API. A Meta anunciou
  coexistência entre o app e a API; a disponibilidade no Brasil deve ser verificada antes de
  prometer isso à Tatiana.
- Fora da janela de 24 h, cada aviso custa uma mensagem de template (utilidade é barato,
  marketing é o mais caro). Atendimento iniciado pelo cliente não é cobrado.
- Se um dia o canal mudar, o que troca é `IWhatsAppCloudClient` e o webhook; conversa, mensagem,
  agente, pedido e avisos ficam.
