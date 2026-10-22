// Apresentação da conversa (#1868): ações visíveis do composer e aviso do pedido no fio.
// Puro e sem JSX, para a prova (`ferramentas/prova-1868-visual.mjs`) conferir a mesma regra que a tela usa.
//
// Medido em 1280 com o chat do site: o composer vazio tinha 136 px, com Anexar,
// Fotos e o microfone apagados ao lado do campo. O canal manda no que aparece;
// o motivo continua escrito na linha da restrição (`restricoesDoCanal`).
export function acoesDoComposer({ podeAnexar = true, podeFoto = true, podeAudio = true } = {}) {
  const anexar = Boolean(podeAnexar)
  const fotos = Boolean(podeFoto)
  // Sem áudio, o lugar do microfone fica com o Enviar, desligado até haver texto.
  const audio = Boolean(podeAudio)
  // Sem os dois ícones de mídia, sobram Respostas, Programar e Nota: cabem ao lado do campo.
  return { anexar, fotos, audio, enxuta: !anexar && !fotos }
}

// Aviso do sistema que é o pedido do site (itens, total, endereço): o texto mais
// importante da conversa estava em 13 px, tinta 3, centralizado em 5 linhas. Ganha
// leitura de documento: à esquerda, no corpo de apoio, com o trilho do post-it.
// Aviso curto ("Conversa reaberta") segue miúdo e centralizado.
const LIMITE_DO_AVISO_CURTO = 140
export function avisoDePedido(texto) {
  const t = String(texto ?? '').trim()
  return t.startsWith('Pedido do site #') || t.length > LIMITE_DO_AVISO_CURTO
}

// #1239: para quem a conversa pode ir. Quem está passando e quem já está com ela não entram.
export function atendentesParaPassar(atendentes, { usuarioId = null, responsavelId = null } = {}) {
  return (atendentes ?? []).filter((a) => a.usuarioId && a.usuarioId !== usuarioId && a.usuarioId !== responsavelId)
}
