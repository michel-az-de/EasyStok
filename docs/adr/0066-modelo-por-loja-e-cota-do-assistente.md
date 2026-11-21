# ADR-0066 · Modelo por loja dentro da lista aprovada e cota mensal do Assistente

- Status: Aceito (2026-11-14; Felipe aprovou no chat da sessão da #2518: "Aprovo, pode mergear")
- Data: 2026-11-13
- Decisor: Felipe
- Relacionados: issue #2518, plano `docs/plan/atendimento-whatsapp/19-engrenagem-do-assistente.md`
  (Onda 14, S67 a S70), ADR-0065 (harness configurável, S66 substituída), ADR-0055 (tiers)

## Contexto

A ADR-0065 deixou a loja ajustar só o tom, a saudação, instruções curtas e o liga e desliga de
habilidade, "nunca modelo, ferramenta, guarda nem gatilho de escalada". Em 13/11/2026 o Felipe pediu uma
engrenagem com prompt, respostas prontas, histórico, cota de tokens e modelo. Ele escolheu, por
múltipla escolha, que a loja possa escolher o modelo entre os aprovados e que exista um limite mensal
com alerta.

## Decisão

1. **Modelo por loja, só dentro da lista aprovada.**
   - `ConfiguracaoAtendimento.ModeloAgente` é anulável; nulo usa o padrão global.
   - A lista vive em `Anthropic:ModelosAprovados`, e a API recusa modelo fora dela.
   - Entrar na lista continua ALTO pela D7 da ADR-0065: exige eval verde com pass^k e custo lado a lado.
   - A loja só escolhe entre modelos que já passaram nessa prova.
2. **Cota mensal de tokens do Assistente por loja.**
   - `ConfiguracaoAtendimento.CotaTokensMes` é anulável; nulo significa sem limite.
   - A partir de 80%, o painel mostra alerta.
   - Em 100%, o turno não chama o modelo, e a conversa vai para a dona pela escalada que já existe. O
     cliente nunca fica sem resposta.
3. **O prompt continua guiado.** A loja edita campos (nome da dona, tom, roteiro em etapas, respostas
   prontas e observações); as regras de preço, pagamento e entrega ficam no código. Não há texto livre
   do prompt.
4. A S66 da Onda 13 é substituída pela Onda 14. As demais decisões da ADR-0065 continuam valendo.

## Alternativas descartadas

- **Só mostrar o modelo em uso.** Mais seguro, mas não atende o pedido de escolha por loja.
- **Texto livre do prompt.** A dona poderia apagar as regras de preço e pagamento sem perceber.
- **Cota que desliga o robô.** O cliente ficaria sem resposta; a escalada para a dona preserva o
  atendimento.

## Consequências

- Cada modelo novo custa um eval antes de aparecer na lista.
- O consumo do Assistente passa a ser medido pelo registro do turno (S67), separado do gerador de
  anúncios. `UsoIa` continua como está.
- A escalada por cota usa o mesmo caminho da escalada atual, então a fila da equipe recebe a conversa
  sem mudança de tela.
