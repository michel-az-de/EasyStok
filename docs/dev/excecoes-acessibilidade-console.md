# Exceções de acessibilidade do Console

Lugar único das regras do axe que o Console aceita descumprir de propósito, com o motivo, quem decidiu e como reavaliar. Exceção nova entra aqui com issue.

## Nome acessível curto do cartão da lista do Atendimento (`label-content-name-mismatch`)

- **Regra do axe:** `label-content-name-mismatch` (serious, WCAG 2.5.3 Rótulo no nome). Acusa 9 cartões: o texto visível do botão (iniciais, nome curto repetido, prévia da mensagem) não está dentro do `aria-label`.
- **O que o Console faz:** o cartão tem `aria-label` composto por `rotuloDoCartao` (`EasyStock.Console/src/features/caixa-de-entrada/apresentacaoDoCartao.js`): nome do contato, estado, mensagens sem resposta, motivo, contexto, prazo estourado, hora e "conversa aberta", sem a prévia. Decisão da banca da #2232 (X1, WCAG 4.1.2): antes o leitor de tela lia o botão inteiro, prévia incluída, a cada cartão.
- **Por que fica:** o nome do contato abre o nome acessível, então quem fala por voz ("clicar em Renan Okamoto") acerta o cartão. Satisfazer a regra exige o texto visível inteiro dentro do nome, na ordem, e devolveria a prévia de cada cartão ao leitor de tela.
- **Decidido por:** Felipe, 25/11/2026, na #3336 (opção B, manter o nome curto).
- **Como reavaliar:** se a atendente usar controle por voz e errar o cartão, ou se o axe virar gate da CI. Aí a opção A: nome com o texto visível mais os extras (estado, prazo, conversa aberta), com `rotuloDoCartao` e a `prova-2232-lista` atualizadas.
- **O que não vale:** esconder o texto visível com `aria-hidden` só para o axe parar de acusar. A regra seguiria descumprida e o relatório passaria a mentir.
