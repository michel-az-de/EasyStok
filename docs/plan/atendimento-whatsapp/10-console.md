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
| `entregas` | S12, S14, S44 | S45 parte 1 sim; o resto não | `api/minha-vitrine/entrega` | a medir na F04 |
| `cozinha` | S19, S20, S21 | Não | SSE do S18 | KDS atual (`Api/Mobile/Controllers/KdsController.cs`) |
| `cardapio`, `cardapio-link` | S45, S48 | Parcial | `api/admin/storefronts/{id}/cardapio`, `api/storefront/{slug}/menu` | a medir |
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
- [ ] Abrir e fechar a loja pelo topo reflete no expediente (validação do Felipe).
- [ ] A configuração salva e recarrega (validação do Felipe).
- [ ] O assistente responde (validação do Felipe).
- [ ] E-mail e SMS da Ficha gravam e voltam após recarregar (validação do Felipe).

**Fora.** Mensagem programada (S39), finalidade Marketing na Ficha, SSE do expediente.
