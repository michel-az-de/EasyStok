# 22. Canhoto da Casa pelo app, no WhatsApp da dona (#3190)

Spec curta, 22/11/2026. Plano de origem: evolução do atendimento (`docs/dev/plano-evolucao-atendimento-2026-11-19.md`),
revisto em 22/11 com duas bancas Opus (com ressalvas, aplicadas).

## Problema

O canhoto da cliente (#2937) só sai pela conexão do sistema e só com conversa na janela de 24 h
(`CanhotoPedidoCliente.cs`). A conexão está em outro número; a dona vende pelo WhatsApp Business do próprio
celular e lança o pedido no app. Medido em 22/11: nenhum canhoto chegou a cliente real.

## Decisão

O canhoto sai do app, pelo WhatsApp do celular da dona. O servidor só desenha a imagem; quem envia é ela.

| Peça | Comportamento |
|---|---|
| `GET api/mobile/pedidos/{mobileOrderId}/canhoto-cliente` | `[MobileApiKey]`, empresa do pareamento. Acha o pedido por `FindByMobileOrderIdAsync`. PNG direto, sem armazenamento público, `no-store`. Selo "Pago" quando há `quitacao_confirmada`. 409 se o pedido ainda não sincronizou ou é de outra empresa. |
| `POST .../canhoto-cliente/aberto` | Grava `canhoto_aberto_para_compartilhar` uma vez por pedido. Mede INTENÇÃO: o seletor abriu; não prova que a cliente recebeu. |
| App, modal de cobrança | Botão "Enviar canhoto": Filesystem em CACHE + Share do Capacitor (o `navigator.share` com arquivo falha calado no WebView). Fora do APK cai no texto da cobrança. Cancelar não registra. |

A imagem traz o código do ERP; o texto que acompanha não leva código, para a cliente não ver dois códigos.

## Medida de resultado (fim)

Contagem absoluta em 30 dias: pedidos lançados no app e pedidos com `canhoto_aberto_para_compartilhar`.
Parada: duas semanas sem nenhum canhoto aberto encerram a frente.

## Fora

Conexão do sistema no número de venda, modelos da Meta, papelaria e assinatura.
