# ADR-0062 · Ponte provisória do WhatsApp pelo WAHA enquanto a Meta não aprova o Tech Provider

- Status: Aceito (2026-10-26, decisão do Felipe no chat da sessão da issue #2072)
- Data: 2026-10-26
- Decisor: Felipe
- Prazo: provisório. Sai quando o número da loja entrar na Cloud API por coexistência (ADR-0050, #1417).
- Relacionados: ADR-0050 (WhatsApp direto na Meta), ADR-0051 (multicanal), issue #2072, pesquisa
  `docs/plan/atendimento-whatsapp/ponte-provisoria-whatsapp-2026-10-26.md`, guia `docs/dev/whatsapp-ponte-waha.md`

## Contexto

O atendimento da Casa da Baba foi construído sobre a Cloud API da Meta (ADR-0050). O número da loja só entra por
coexistência, que exige o Tech Provider do app EasyStok. Em 26/10 o App Review estava em rascunho, sem envio. A
operação não pode esperar: sem canal, o agente, os avisos de pedido e a cobrança ficam mudos.

A comunidade (GitHub, fóruns e blogs, pesquisa de 26/10) oferece pontes que ligam um número comum como aparelho
vinculado: Evolution API, WAHA, wuzapi, WPPConnect, whatsapp-web.js e Baileys. Todas violam os Termos do WhatsApp e
podem levar ao banimento permanente do número. Os relatos de 2025 e 2026 (Baileys #1869) apontam detecção por padrão
de uso: volume, rajada, grupos e número novo.

## Decisão

1. **WAHA com o motor GOWS (whatsmeow)**, imagem fixada (`devlikeapro/waha:gows-2026.9.2`), como stack própria na
   VPS (`scripts/deploy/waha/compose.yaml`), na rede interna, sem porta pública.
2. **Número principal da loja.** O Felipe escolheu o número que os clientes já conhecem, aceitando o risco de
   banimento em troca de não mudar o contato da loja. O celular continua com o app; o WAHA é um aparelho vinculado.
3. **Mesma porta da Cloud API.** `WahaWhatsAppClient` implementa `IWhatsAppCloudClient`; o webhook do WAHA traduz o
   evento para o corpo da Meta e chama o mesmo `ProcessarEventoWhatsAppUseCase`. Domínio, conversa, agente, outbox e
   console não mudam. A chave é `Atendimento:WhatsApp:Cliente=waha`; o número de plataforma (N6) continua na Meta.
4. **Paridade com a Meta.** A ponte manda o que a Meta mandaria, nada a mais. A janela de 24 h continua valendo. Onde
   o outbox usaria modelo aprovado, sai o texto da própria mensagem (`ICanalSemModeloAprovado`); sem modelo, fora da
   janela continua recusado. Envio de modelo pelo console falha com motivo explícito.
5. **Comportamento de gente.** "Digitando" e pausa proporcional ao texto antes de cada envio, um envio por vez por
   processo, só conversa individual (grupo, status e canal ignorados na sessão e no tradutor).
6. **Botões viram opções numeradas.** A resposta "2" ou o título da opção volta como clique do botão.

## Alternativas consideradas

- **Esperar a Meta.** Recusada: a operação precisa funcionar já.
- **Evolution API.** Maior comunidade no Brasil, mas em 26/10 a última versão estável era de 12/2025, a 2.4 exige
  ativação de licença e o motor Baileys é o mais citado nos relatos de banimento.
- **wuzapi.** Mesmo motor do WAHA, gratuito, mas API mais crua e sem painel. Fica como alternativa se o WAHA falhar.
- **Chip secundário.** Menor dano em caso de banimento, mas muda o contato que os clientes conhecem. Recusada pelo
  Felipe.
- **SaaS pagos por QR (Z-API e similares).** O mesmo risco, sem controle da infraestrutura.

## Consequências

- Risco aceito: o número da loja pode ser banido sem recurso. O primeiro sinal (restrição de conversa nova ou cota)
  aparece no log da API como erro.
- Sem modelo aprovado, mensagem programada e reenvio de modelo fora da janela falham com motivo explícito.
- O menu numerado vive no cache da aplicação: sem Redis compartilhado, a resposta a um menu enviado pelo Worker chega
  ao agente como texto.
- Conversa que o WhatsApp entrega só pelo id oculto (`@lid`) usa o telefone que o WAHA conhece; sem telefone, o
  contato fica com o id oculto.
- Saída: com o Tech Provider aprovado, volta `Atendimento:WhatsApp:Cliente=meta`, a empresa é vinculada ao
  `phone_number_id` da Meta e o aparelho do WAHA é desconectado. O código da ponte pode ficar desligado ou sair numa
  poda.
