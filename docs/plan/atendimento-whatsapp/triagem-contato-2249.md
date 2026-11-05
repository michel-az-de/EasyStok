# Triagem de contato que não é cliente (#2249)

## Problema

Em 05/11 um fornecedor pediu retorno sobre um orçamento para a loja. A IA respondeu que a loja não tinha
interesse e encerrou. Na conversa seguinte a mesma pessoa recebeu a saudação de cliente com o cardápio e a
frase fixa de encaminhamento. A equipe recebeu dois avisos e a pessoa, três mensagens que não serviam.

## Decisão

| Situação | Quem decide | O que o contato recebe | O robô depois |
|---|---|---|---|
| Cliente pede gente, reclama, pedido fora do cardápio | IA, `escalar_para_dona` | Molde **escalada** | Cala nesta conversa |
| Fornecedor, orçamento, banco, contador, vaga, parceria, pessoal | IA, `encaminhar_para_responsavel` | Molde **encaminhamento** | Cala para a PESSOA, em toda conversa |
| Pessoa pausada abre conversa nova | Sistema, sem LLM | Nada | Conversa nasce com a equipe, um aviso |
| Equipe clica "devolver ao robô" | Equipe | Nada | Pausa da pessoa sai |

- A IA decide QUANDO encaminhar. O texto ao contato é molde da loja, com `{nome}` (primeiro nome do
  cadastro ou do perfil do WhatsApp). Texto livre do LLM nesse ponto já era descartado; o molde dá
  previsibilidade e a loja edita na aba Atendimento.
- A pausa fica no `Cliente` (`AtendimentoAutomaticoPausadoEm`, `MotivoPausaAtendimento`), igual ao bloqueio,
  porque a conversa muda e a pessoa não.
- Sem saudação nem automática de entrada para pessoa pausada: o cardápio não serve a quem não é cliente.

## Moldes padrão

- Escalada: `Vou encaminhar sua mensagem para a equipe conferir e responder por aqui.` (o texto fixo de hoje)
- Encaminhamento: `Oi, {nome}! Eu sou o atendimento automático da Casa da Baba. Já passei sua mensagem para a
  responsável, que te responde por aqui.`

Sem nome, `{nome}` some junto da vírgula ("Oi! Eu sou...").

## Fora

Escape HTML no sino, telefone no aviso quando a conversa não tem nome, dedup do aviso de atenção.
