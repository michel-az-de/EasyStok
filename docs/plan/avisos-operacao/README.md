# Avisos da operação (#2096)

Pedido do Felipe em 27/10/2026: avisar a dona e o gerente, por push no navegador, e-mail e sino, nas situações que travam a operação, tudo configurável. Decisões dele no mesmo dia: pacote único (fases 1 a 4), críticos para `gestores` (Admin e Gerente), o tempo da fila é o SLA da aba Atendimento, impressão automática num PC com Chrome na cozinha.

## O que já existia e foi reaproveitado

| Peça | Onde |
|---|---|
| Motor de avisos (outbox, dedupe por `CorrelationId`, validade, janela, audiência) | `EasyStock.Application/Services/Notifications`, loops no Worker |
| Web Push VAPID e assinatura por aparelho | `WebPushCanal`, `PwaPushController`, `sw-avisos.js` |
| E-mail SMTP (`avisos@`) | `SmtpEmailCanal`; **em produção ainda está em `console`** |
| Prazo estourado com 4 produtores | `PrazoEstouradoEvento`, jobs `PedidoAtraso`, `ImpressaoPendenteAlerta`, `AvaliadorLembretes`, `CaixaEsquecido` |
| Escalada da IA para a dona (push) | `escalar_para_dona`, `ConversaEscalada` |
| Push de etapa do pedido para a equipe | `AvisoEquipeNoAparelho` |
| Fila de impressão e canhoto | `ImpressaoController`, `FilaCanhotos.jsx` |

## O que entrou

```
fato do sistema / vigia / IA
        │  payload com "aviso": <código>
        ▼
NotificadorComAvisosDaLoja ── aviso desligado na tela? ── sim ──> não enfileira
        │ não
        ▼
NotificadorService (motor) ──> Push + E-mail + Sino para gestores
```

| Aviso | Produtor | Tempo (tela Avisos) | Canal |
|---|---|---|---|
| Cliente esperando resposta | lembrete existente | SLA da aba Atendimento | Push, e-mail, sino |
| Pedido sem início de preparo | `PedidoAtrasoJob` | início previsto | Push, e-mail, sino |
| Impressão travada | `ImpressaoPendenteAlertaJob` | 3 min (config) | Push, e-mail, sino |
| Caixa aberto de ontem | `CaixaEsquecidoJob` | dia anterior | Push, e-mail, sino |
| Pedido pronto sem entregador | `VigiaOperacaoJob` | 15 min (5 a 240) | Push, e-mail, sino |
| Encomenda chegando | `VigiaOperacaoJob` | 2 h antes (1 a 48) | Push, e-mail, sino |
| Encomenda sem confirmação | `VigiaOperacaoJob` | 24 h antes (1 a 72) | Push, e-mail, sino |
| Estoque para conferir (freezer) | `VigiaOperacaoJob`, 1 vez por dia após 8 h | vence em 2 dias (1 a 30); parado há 10 dias (3 a 120) | Push, e-mail, sino |
| Reclamação do cliente | IA, `avisar_gestao` | — | Push, e-mail, sino |
| Algo suspeito na conversa | IA, `avisar_gestao` | — | Push, e-mail, sino |

Tempo 0 desliga. "Pronto sem entregador" é pedido em `pronto`, com endereço de entrega, sem viagem ativa com entregador e sem corrida chamada (despacho Lalamove de pé). A cotação Lalamove, congelada, não foi tocada: a vigia só lê.

"Estoque parado" é regra, não IA: lote finalizado com etiqueta sem baixa e produzido há mais que o limite. Uma leitura por IA fica para depois, se a regra mostrar falso positivo demais.

## Dados

- `configuracoes_atendimento.avisos_operacao` (jsonb, padrão `{}`): o documento `AvisosOperacao`. Linha antiga lê o padrão de cada campo.
- `pedidos.confirmado_pelo_cliente_em` (timestamptz, nulo): marcado pelo botão "Cliente confirmou", pela IA (`confirmar_encomenda`) ou desfeito pela equipe.
- `TipoEventoNotificacao.AlertaOperacao = 91`, templates `alerta_operacao_{push,inapp,email}_v1`, rotina `alerta_operacao_global`. A rotina `prazo_estourado_global` ganha o canal Push.

## API

| Rota | Política |
|---|---|
| `GET/PUT api/atendimento/avisos` | Admin |
| `POST/DELETE api/pedidos/{id}/confirmacao-cliente` | Operador |
| `GET api/kds/pedidos` passa a trazer `confirmadoPeloClienteEm` | — |

## Impressão automática

Por aparelho (localStorage), ligada em Gestão › Impressão. Com o console aberto, uma aba só (Web Locks) confere a fila de impressões a cada 10 s, imprime o canhoto de cozinha num iframe oculto e marca `impressa` ou `falhou`. Pendentes criadas antes de ligar ficam de fora. Para não abrir a janela de impressão, o Chrome do PC da cozinha abre com `chrome.exe --kiosk-printing --app=<endereço do console>` e a térmica é a impressora padrão do Windows.

## Para ir ao ar

1. Configurar `Smtp__*` no `.env` da API e do Worker (sem isso o e-mail fica `Simulado`; push e sino funcionam).
2. Na dona e no gerente: "Ativar avisos neste aparelho" no sino do console.
3. Ajustar os tempos na tela Avisos.
4. No PC da cozinha: atalho do Chrome com `--kiosk-printing` e ligar a impressão automática.

## Rollback

Desligar cada aviso na tela; `BackgroundJobs:EnableVigiaOperacao=false` para o job; `Notifications:Prazos:Habilitado=false` para todos os prazos; `Ativa=false` na rotina `alerta_operacao_global`. A migração é aditiva e o `Down` remove as duas colunas.

## Fora deste pacote

Conferência de pedido com câmera e foto para laudo de reclamação (fase 5); classificação de estoque por IA; aviso ao cliente pedindo confirmação da encomenda (depende de modelo aprovado na Meta).
