# ADR-0067 · A loja liga e desliga as ferramentas do Assistente, menos a saída para a equipe

- Status: Proposto (aguarda a label `aprovado` na PR da #2853)
- Data: 2026-11-18
- Decisor: Felipe
- Relacionados: issue #2853, ADR-0065 (harness configurável), ADR-0066 (modelo por loja e cota),
  plano `docs/plan/atendimento-whatsapp/19-engrenagem-do-assistente.md` (Onda 14)

## Contexto

A ADR-0065 e o contexto da ADR-0066 deixaram a loja fora das ferramentas: "nunca modelo, ferramenta,
guarda nem gatilho de escalada". Em 18/11/2026 o Felipe pediu uma tela do Assistente onde desse para ver
o fluxo do atendimento "tipo n8n" e ligar e desligar coisas. Por múltipla escolha, ele escolheu ter, já
nesta entrega, uma chave por loja para cada ferramenta.

## Decisão

1. **Chave por loja para cada ferramenta.** `ConfiguracaoAtendimento.FerramentasDesligadasJson` (jsonb,
   anulável) guarda os nomes desligados. Nulo ou vazio quer dizer todas ligadas.
2. **A saída para a equipe não desliga.** `escalar_para_dona`, `encaminhar_para_responsavel` e
   `avisar_gestao` são travadas no código (`FerramentasDaLoja.Travadas`). Nome travado ou desconhecido
   é recusado no PUT, com 400.
3. **Desligar só retira, nunca acrescenta.** O turno filtra o registro de ferramentas antes de enviá-lo
   ao modelo. Se o modelo pedir uma ferramenta desligada, ela não roda e o turno recebe "Ferramenta
   desconhecida". O prompt ganha uma linha com as ferramentas desligadas e manda passar à equipe o que
   dependia delas. Sem nada desligado, o prompt fica igual byte a byte, e o snapshot não muda.
4. **As guardas, o limite por hora e a cota continuam no código.** Na tela elas aparecem como etapas
   fixas, só para leitura.

## O que muda na ADR-0065

- A frase "nunca ferramenta" da D4 deixa de valer para **desligar** uma ferramenta já registrada. Criar
  ferramenta, conceder permissão e desligar guarda continuam proibidos.
- A D7 pede eval verde para "ferramenta liberada". Desligar reduz o que o modelo pode fazer e não
  libera nada novo, mas mesmo assim muda o comportamento do turno. A pasta `agents/` com o eval da D5
  ainda não existe, então esta decisão entra sem eval. Isso fica registrado como lacuna: quando o eval
  existir, ele ganha um caso por ferramenta desligada.

## Alternativas descartadas

- **Só mostrar as ferramentas.** Atende o "ver o fluxo", mas não o "ligar e desligar".
- **Liberar todas as chaves, inclusive a escalada.** O cliente poderia ficar sem caminho até uma pessoa.
- **Ferramentas por perfil em arquivo (D3 da ADR-0065).** É o certo para o time, mas a dona não edita
  arquivo. A chave da loja fica por cima do perfil e só retira.

## Consequências

- A dona pode desligar `enviar_link_pagamento` ou `criar_pedido`. Nesse caso, o Assistente passa à
  equipe em vez de fechar o pedido.
- A tela mostra o estado real, porque o fluxo e o turno leem a mesma configuração.
- Lacuna conhecida: não há eval do agente (`agents/` ausente).
