### Fixed
- **Atendimento mais resistente a queda passageira** (#2285): a mensagem programada que falha por conexão ou limite de taxa tenta de novo (até 3 vezes, com recuo) em vez de virar "Falhou"; o agente refaz uma vez a chamada ao LLM antes de escalar a conversa para a dona; a fila em memória registra a exceção inteira no log em vez de só a mensagem no console.
