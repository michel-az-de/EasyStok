# ADR-0063 · Linguagem do console: um conceito, um nome, um ícone, um componente

- Status: Proposto (aguarda a label `aprovado`)
- Data: 2026-10-29
- Decisor: Felipe
- Relacionados: issue #2144, guarda-chuva #2130, plano `docs/plan/balcao-ux/README.md` (aprovado em
  28/10, seções 2 e 3), relatórios da banca em `docs/plan/balcao-ux/banca/`, #1777 (shell global),
  #2140 (harmonia de linguagem), ADR-0011 e ADR-0018 (nomenclatura do código), ADR-0054 (console no
  repositório), ADR-0055 (tier de spec de plano aprovado), ADR-0056 (front único)

## Contexto

O Felipe, que é desenvolvedor, não conseguiu operar o Balcão. Em 28/10 a banca mediu a causa no
código e na tela real, em 390, 1280 e 1920 px:

| Sintoma | Medida |
|---|---|
| Um nome com vários sentidos | "Balcão", "Resumo" e "Atendimento" têm 4 sentidos cada |
| Vários nomes para uma coisa | A IA aparece com 6 a 8 nomes; o relógio, com 7 sentidos |
| Ícones | 2 bibliotecas desenhadas à mão, chaves duplicadas (`lupa`/`search`, `fechar`/`x`) e nenhuma engrenagem |
| Botões | 54 botões só com ícone, 37 sem tooltip, 100 `title` nativos |
| Feedback | 6 mecanismos diferentes para avisar o resultado de uma ação |
| Movimento | 6 animações infinitas; Folha e Modal sem animação de saída |

Cada PR acertava a sua tela, e a tela vizinha continuava falando outra língua. Uma regra que só
existe no plano não sobrevive à próxima PR. Por isso a gramática precisa de um ADR e de guardas
que falhem no CI.

## Decisão

### D1 · Vocabulário

O que aparece na tela segue a tabela 3.1 do plano. Entre os nomes estão Conversas, Conversa,
Cliente, Pedido, Enviar cardápio, Cobrar, Concluir conversa, Passar conversa e Assistente. Os termos
internos (Canhoto, Lead, Peça, esteira, Console, Hall, Trilho, "a dona") não aparecem para quem
opera. O código continua com os nomes do ADR-0011 e do ADR-0018: esta regra vale só para o texto da
tela.

### D2 · Uma biblioteca de ícones

O console usa só o `lucide-react`. O componente `Icone` passa a ser uma fachada sobre ele: cada
chave aponta para um único ícone, e o sentido de cada conceito segue a tabela 3.2 do plano. Alguns
exemplos: a engrenagem é Configurações, ✨ é o Assistente, ⋮ é Mais opções, `log-out` serve só para
sair da conta, e Concluir conversa é `circle-check`. Um ícone novo só entra pelo dicionário do
`Icone`. Os ícones da marca (`MarcaIcone`) continuam onde estão até a fachada absorvê-los. Esse
passo precisa de PR própria, porque muda o visual dos módulos.

### D3 · Componentes obrigatórios

| Componente | Regra |
|---|---|
| `Tooltip` | Aparece no hover e no foco depois de 500 ms, com "Verbo + objeto" e o atalho. Nunca aparece em toque. Substitui o `title` nativo |
| `BotaoIcone` | `rotulo` obrigatório. Gera sozinho o nome acessível e o Tooltip. Alvo de `var(--toque)` |
| `Aviso` | Snackbar no pé da tela, com 3 tons, 1 por vez e "Desfazer" opcional. Responde à ação da própria pessoa. A pilha do sino (`Toast`) é outra coisa |
| `Selo` | Estado mostrado com cor, ícone e texto, nunca só com cor. No máximo 4 estados de conversa |
| `Folha` | Painel lateral no desktop e bottom sheet no celular. Não abre modal sobre modal |
| `Modal` | 3 larguras (P, M, G). Rodapé com "Cancelar" e o botão primário à direita |
| `Vazio` | Uma frase e a próxima ação |

Tela nova ou alterada usa esses componentes. Tela antiga migra quando for tocada, e o teto da D6
impede que o problema cresça.

### D4 · Movimento

As durações e curvas saem dos tokens que já existem em `tokens.css`: `--dur-*` e `--ease-*`. A
troca de estado usa `--dur-curta` (150 ms) e a entrada de camada usa `--dur-media` (220 ms). A
saída é sempre mais curta que a entrada. Com `prefers-reduced-motion`, o `base.css` zera o
movimento. Urgência pulsa uma vez quando chega e depois fica parada; animação infinita não é usada.

### D5 · Copy

O texto de um botão é um verbo com o objeto ("Cobrar R$ 84,00"). Para dispensar, cada palavra tem
um uso:

- **Cancelar** desiste de um diálogo.
- **Fechar** sai de um painel sem mudar nada.
- **Voltar** serve só para navegar.

Mensagem de erro diz o que aconteceu e o que fazer, sem culpar a pessoa e sem "Ops!". Estado vazio
sempre traz a próxima ação. A voz da Casa da Baba fica no que o cliente vê. O console fala de
operação.

### D6 · Guardas por teto

O script `EasyStock.Console/ferramentas/verificar-gramatica.mjs` roda no `npm run qualidade` e no
CI do console. Ele conta 4 coisas:

1. Chave de ícone fora do dicionário. Teto: 0.
2. `<button>` cru em `features/atendimento` e `features/caixa-de-entrada`.
3. Termo proibido no texto da tela.
4. Duração ou curva escrita direto no CSS, sem token.

O teto de cada contagem começa no número medido e só pode descer. Quando a contagem fica abaixo do
teto, o script pede para baixar o teto. Subir um teto exige justificativa na PR, e a PR vira tier
alto.

### D7 · Escopo e ordem

A gramática começa no Atendimento (ondas 1 a 3 do plano) e chega aos outros módulos depois que as
atendentes validarem (onda 4, #1777). O APK piloto vai consumir a mesma gramática quando tiver
interface própria. Biblioteca nova, além do `lucide-react`, precisa de outro ADR.

## Consequências

- O revisor tem critérios objetivos: nome, ícone e componente estão no dicionário ou não estão.
- O `lucide-react` vai empacotado no build, sem CDN. Entram só os ícones importados, e o aumento do
  bundle é medido na PR que o adota.
- As provas que procuram texto (rótulos e `title`) mudam junto com a copy. Mudar o que a prova
  espera exige o mesmo cuidado que mudar o contrato funcional.
- Um ícone da biblioteca pode trocar o desenho de uma chave que já existia. Esse caso é visível e
  fica listado na PR da fachada.
- Custo: as telas antigas continuam fora da regra até alguém tocar nelas. É o preço aceito para
  não fazer uma migração grande de uma vez.

## Alternativas descartadas

| Alternativa | Por que não |
|---|---|
| Material Symbols Rounded | Trocaria o visual inteiro. O Lucide tem o mesmo traço que já está na tela (decisão 2 da seção 9 do plano) |
| Continuar com ícones desenhados à mão | É a origem das duplicatas e dos 7 sentidos do relógio |
| Biblioteca de componentes pronta (Radix, MUI) | Dependência grande para 7 componentes simples, e o tema e os tokens teriam de ser refeitos |
| Regra só no plano, sem guarda | Já foi tentado. Cada PR acerta a sua tela e a vizinha regride |
