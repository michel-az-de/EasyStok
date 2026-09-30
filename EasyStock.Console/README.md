# EasyStok · console do operador

Console do operador do EasyStok (ADR-0054). Nasceu como protótipo do atendimento omnichannel
da Casa da Baba, a partir da entrevista de 21/09/2026 com a Thatiane, dona e cozinheira, no
repositório `michel-az-de/casa-da-baba-atendimento` (`prototipo-omni/`, commit a9831ab). Lá ficam
o histórico, a transcrição, as estórias e a pasta `auditoria/` citada nos comentários.

Hoje roda com dados simulados. A ligação à API real é feita módulo a módulo, na ordem da
[matriz de paridade](../docs/plan/atendimento-whatsapp/10-console.md).

## Rodar

| Comando | O que faz |
|---|---|
| `npm run dev` | Tela com recarga automática em `http://localhost:5173`. O caminho `/api` vai por proxy ao servidor do agente |
| `npm run agente` | Servidor local do agente em `http://127.0.0.1:5245`. Necessário para os modos CLI e API |
| `npm run local` | Gera o `dist/` e sobe o servidor do agente servindo a tela na mesma porta. Um processo só |
| `npm run qualidade` | Lint, verificação de fronteira de camadas e build. Os três precisam passar |

Para testar de um tablet na mesma rede: `HOST_AGENTE=0.0.0.0 npm run local` e abra o IP
do computador na porta 5245. Sem essa variável o servidor só atende a própria máquina.

## Modos do agente

| Modo | Onde roda | Precisa de |
|---|---|---|
| Simulado no navegador | Rascunho determinístico do domínio, nada sai da máquina | Nada |
| Claude pela linha de comando | `servidor/agente.mjs` roda `claude -p` com o login do Claude Code | Claude Code instalado e logado |
| Claude pela API | O mesmo servidor chama a API pelo SDK oficial | `ANTHROPIC_API_KEY` no ambiente do servidor |

Variáveis do servidor: `PORTA_AGENTE` (5245), `HOST_AGENTE` (127.0.0.1), `MODELO_CLI`
(vazio: o padrão do Claude Code) e `MODELO_API` (`claude-opus-5`).

O prompt enviado ao modelo fica visível no painel do agente, de propósito. A resposta
volta como JSON com `acao` e `texto`. Quando a ação é `passar_para_dona`, o botão
"Enviar assim" some: restrição alimentar, item sem saldo e reclamação nunca saem no
automático.

## Massa de conversas

`dados/massa-conversas.json` tem 30 conversas de teste com o resultado esperado do
agente em cada uma. Ela é o padrão em qualquer build. Com `?massa=0` na URL a tela
volta à semente curta de 5 conversas, útil para demonstração rápida.

## Arquitetura

Camadas com dependência só para baixo, verificada por `ferramentas/verificar-camadas.mjs`:
`dominio` (puro) ← `infra` ← `aplicacao` ← `features` ← `app`. Feature não importa feature.
Os geradores da primeira versão (`ferramentas/*.py`) ficaram no repositório de origem.
