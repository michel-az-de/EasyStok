# Console do operador — matriz de paridade e ordem de ligação

Decisão: ADR-0054 · Issue: #1120 · Código: `EasyStock.Console/` · Medido em 2026-09-29 no master 69d9f9b3

Cada linha é um módulo do console. Ele é **ligado** quando troca o repositório simulado
(`src/infra/repositorio*.js`, massa em `dados/`) pela API real. Só se liga um módulo cuja spec
backend já está no master. Quando o módulo fica em paridade, a tela legada da última coluna sai.

## Matriz

| Módulo do console (`src/features/`) | Spec backend | No master? | Endpoint | Tela legada que sai |
|---|---|---|---|---|
| `caixa-de-entrada`, `atendimento` (lista, thread, assumir, responder, encerrar) | S04, S07, S34 | Sim | `api/atendimento/conversas` (+ `/mensagens`, `/assumir`, `/liberar-automatico`, `/encerrar`, `/marcar-lida`) | Nenhuma |
| `anexos` | S02 | Sim | `api/atendimento/conversas/{id}/mensagens/imagem` | Nenhuma |
| `agente` (painel do automático) | S06 | Sim | o agente roda na API; o console só mostra estado e sugestão | `servidor/agente.mjs` do próprio console |
| `assistente` (ligado na F02) | S47 | Sim | `api/atendimento/assistente` | Nenhuma |
| `gestao` (expediente, configuração; ligado na F02) | S08, S40 | Sim | `api/atendimento/configuracao`, `api/atendimento/expediente` | Nenhuma |
| `ficha-cliente` (consentimento; ligado na F02) | S38 | Sim | `api/atendimento/clientes/{id}/consentimentos` | Nenhuma |
| Mensagem programada (dentro de `atendimento`) | S39 | Sim | `api/atendimento/mensagens-programadas` | Nenhuma |
| `encerramento` (pedido e cobrança) | S10, S11 | Não (S10 na PR #1112, S11 na issue #1115) | `api/storefront/{slug}/checkout` e o do S11 | PWA do caixa (`Api/wwwroot/pwa`), a medir na F03 |
| `entregas` (ligado na F04) | S12, S14, S44, S45 | Sim | `api/kds/pedidos` (+ endereço e aprovação), `api/atendimento/entregadores`, `api/atendimento/viagens`, `api/atendimento/chamados-entregador`, `api/storefront/pedidos/{id}/aprovar` e `/recusar`, `api/minha-vitrine/entrega`, `api/operacao/eventos` (SSE) | Nenhuma |
| `cozinha` (ligado na F05) | S18, S19, S20, S21 | Sim | `api/kds/pedidos` (+ `/{id}/status`), `api/operacao/eventos` (SSE), `api/pedidos/{id}/canhoto`, `api/pedidos/{id}/reimprimir` | KDS atual (`Api/Mobile/Controllers/KdsController.cs`), sai na P05 |
| `cardapio`, `cardapio-link` | S45, S48 | Parcial | `api/minha-vitrine/cardapio`, `api/minha-vitrine/configuracao`, `api/storefront/{slug}/menu` | a medir |
| `ficha-cliente` (tags, notas, dossiê), `notas` | S24, S25 | Não | a definir na spec | a medir |
| `respostas`, `automacoes` | S42 | Não | a definir na spec | Nenhuma |
| `lembretes` | S43 | Não | a definir na spec | Nenhuma |
| `lote-papel` | S46 | Não | a definir na spec | Nenhuma |
| `simulacoes` | nenhuma | N/A | fica só no modo demonstração | Nenhuma |

Estoque, rotulagem e o restante do EasyStock.Web não têm tela no console. Continuam no legado até
existir decisão própria, depois das ondas acima.

## Ordem

```
F01 caixa de entrada real ──► F02 gestão + assistente + consentimento ──► F03 pedido e cobrança (após S10/S11)
        │                                                                        │
        └── login JWT + tenant, deploy em app.easystok.online                     └──► F04 entregas ─► F05 cozinha (após S18–S21)
```

## F01 · Caixa de entrada na API real

**Problema.** A dona não tem onde ver e responder as conversas que já chegam pelo webhook da Meta.
A API (`api/atendimento/conversas`) está em produção desde 29/09; a tela só existe com dados falsos.

**Abordagem.** Um cliente HTTP em `src/infra/api/` com login (`api/auth`) e o token com a empresa;
`repositorioConversas.js` passa a ler da API quando `VITE_API_URL` está definido e cai na massa
quando não está (o modo demonstração continua). Assumir, responder, liberar o automático, encerrar e
marcar como lida chamam os endpoints do S07. Atualização por polling curto até o SSE do S18 existir.

**Aceite.**
- [x] Com `VITE_FONTE_DADOS=api`, a lista mostra as conversas do tenant logado e nenhuma de outro
  tenant (validado pelo Felipe em 2026-09-30, Demonstração EasyStok).
- [x] Mensagem enviada pelo console chega ao WhatsApp do cliente e aparece na thread (validado pelo
  Felipe em 2026-09-30, envio e recebimento).
- [x] Sem `VITE_FONTE_DADOS`, o console abre como hoje, com a massa.
- [x] `npm run qualidade` verde; a fronteira de camadas continua valendo (`features` não chama
  `fetch`).
- [ ] Console publicado em `app.easystok.online` atrás do Caddy compartilhado (GO de deploy próprio).

**Fora.** SSE, cobrança, cozinha, agente no navegador.

## F02 · Gestão, assistente e consentimento na API real

Issue #1201. Base: a F01 (`feat/console-caixa-real-f01-1132`).

**Problema.** No modo API, abrir e fechar a loja, editar o horário, configurar o atendimento, perguntar
ao assistente e marcar os avisos da Ficha mudavam só a memória do navegador; recarregar perdia tudo.

**Abordagem.** Um módulo por endpoint em `src/infra/api/` (`expedienteApi`, `configuracaoApi`,
`assistenteApi`, `consentimentosApi`) e as ações em `src/aplicacao/api/`, compostas em `comApi`:
- Expediente (S40): carrega ao entrar e ao abrir a aba; o botão do topo grava `ForcarAberta` ou
  `ForcarFechada`; editar o horário em Mensagens automáticas grava a semana (espera de 800 ms);
  a aba **Atendimento** da Gestão (só no modo API) tem "Voltar a seguir o horário" e as mensagens de
  fora do horário e loja fechada. Dia sem turno na API é dia fechado no console.
- Configuração (S08): mesma aba, formulário com tom, sugestões, saudações, frases, respiro, preparo e
  a chave do automático; o que a API devolve no PUT volta para o formulário.
- Assistente (S47): o balão pergunta à API com o `conversaId`; o 503 sem chave da Anthropic aparece
  como erro no próprio balão.
- Consentimento (S38): os checkboxes E-mail e SMS da Ficha leem e gravam a finalidade Transacional
  daquele canal e mostram `podeEnviar` (transacional sai por padrão; desmarcar revoga). Só com
  `clienteId`; lead sem cadastro vê o aviso para cadastrar.

Erro de carga ou gravação aparece na FaixaApi (expediente) ou ao lado do controle (configuração,
avisos, assistente); no erro do controle manual o estado volta ao que a API tem.

**Aceite.**
- [x] `npm run qualidade` verde; fronteira de camadas ok.
- [x] Sem `VITE_FONTE_DADOS`, o console abre como hoje, com a massa (sem a aba Atendimento).
- [x] Abrir e fechar a loja pelo topo reflete no expediente (validado pelo Felipe em 2026-09-30).
- [x] A configuração salva e recarrega (validado pelo Felipe em 2026-09-30).
- [x] O assistente responde (validado pelo Felipe em 2026-09-30).
- [x] E-mail e SMS da Ficha gravam e voltam após recarregar (validado pelo Felipe em 2026-09-30).

**Fora.** Mensagem programada (S39), finalidade Marketing na Ficha, SSE do expediente.

## F05 · Cozinha na API real

Issue #1218. Sem endpoint novo: os quatro já estavam no master (S18 a S21).

**Abordagem.** No modo API, `#/cozinha` abre `TelaCozinhaApi` (pede login se a aba não tem sessão);
sem `VITE_FONTE_DADOS` segue a cozinha espelhada do Balcão.
- Fila: `GET api/kds/pedidos` (aguardando, em preparo, pronto e saiu para entrega), uma coluna por status.
- Ao vivo: `GET api/operacao/eventos` lido por `fetch` em stream com o JWT no header (o EventSource
  não manda header). Todo `ready` e `pedido.*` recarrega a fila. SSE caído: fila a cada 15 s e nova
  conexão a cada 10 s.
- S21: o cartão mostra "Começar em N min" ou "Atrasado N min" pelo `inicioPrevistoEm` e `atrasado`.
- Um toque: `PATCH api/kds/pedidos/{id}/status`; a transição inválida volta a mensagem da API.
- S20: "Canhoto" abre o HTML de 80 mm e chama a impressão do navegador; "Reimprimir na fila" põe o
  canhoto de novo na fila do bridge.
- Prova pura: `node ferramentas/prova-f05-cozinha-api.mjs` (próximo passo, aviso S21, leitor SSE).

**Aceite.**
- [x] `npm run qualidade` verde; fronteira de camadas ok.
- [x] Sem `VITE_FONTE_DADOS`, o console e a cozinha abrem como hoje.
- [ ] Pedido pago aparece na cozinha e muda de passo pela tela (validação do Felipe).

**Fora.** Arrastar cartão, filtro por linha e escolha de entregador no modo API; consumo automático
da fila de impressão pela aba (decisão da onda 0.6).

## F04 · Entregas na API real

Issue #1221. Sem endpoint novo. Medido: S44 e S45 já tinham todos os endpoints; faltava ao KDS o
endereço do cliente (S14) e o `RequerAprovacao` com o motivo (S12). Os três campos entraram de forma
aditiva em `KdsPedidoDto`, com `EmpresaId` no WHERE da leitura do cliente e teste de isolamento.

**Abordagem.** No modo API, a gaveta abre `GavetaEntregasApi` e `#/entregas` abre `TelaEntregasApi`
(pede login se a aba não tem sessão). Sem `VITE_FONTE_DADOS`, nada muda.
- Hoje: `GET api/kds/pedidos?status=aguardando_aprovacao_baba,pronto,saiu_para_entrega` e as viagens
  da S44. Recarrega a cada `ready` e `pedido.*` do SSE da F05; SSE caído, a cada 15 s.
- S12/S14: aprovação da exceção (motivo legível, "Entrega fora da área") com aprovar e recusar.
- S44: nova viagem, pôr pedido, subir, descer, tirar, escolher entregador, "Saiu para entrega"
  (bloqueado na tela sem entregador, RN-32), marcar entregue, desfazer, link de rota; entregadores
  (cadastrar e desativar) e chamados (abrir, atendido, cancelar).
- S45: janelas, zonas (CEP ou bairros) e bloqueios dos próximos 60 dias. Policy `Admin`: operador
  comum vê o 403 na faixa.
- Prova pura: `node ferramentas/prova-f04-entregas-api.mjs`.

**Aceite.**
- [x] `npm run qualidade` verde; fronteira de camadas ok.
- [x] Sem `VITE_FONTE_DADOS`, a gaveta e a janela de Entregas abrem como hoje.
- [ ] Viagem com entregador sai e os pedidos vão para "saiu para entrega" (validação do Felipe).

**Fora.** Arrastar parada, editar entregador, editar janela e zona já criadas (só ativar e desativar),
relatório por bairro e o endereço de `ClienteEndereco` (a leitura usa os campos primários do cliente).
