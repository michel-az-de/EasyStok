// Caderno do agente (#2070, S54): o que a loja sabe e o agente do atendimento usa ao responder.
// Trecho do núcleo vai em toda resposta; os demais entram como uma linha de índice e o agente lê o
// texto quando o assunto aparece. Limites e normalização iguais aos de `TrechoCaderno`.
export const LIMITES_DO_CADERNO = { titulo: 120, texto: 4000, palavrasChave: 300, nucleo: 6000 }

export const TRECHO_VAZIO = { titulo: '', texto: '', palavrasChave: '', nucleo: false }

const numero = (n) => n.toLocaleString('pt-BR')

// Minúsculas, separadas por ", ", sem vazias nem repetidas; aceita vírgula ou ponto e vírgula.
export const normalizarPalavrasChave = (texto) => [...new Set(String(texto ?? '')
  .split(/[,;]/).map((p) => p.trim().toLowerCase()).filter(Boolean))].join(', ')

export const trechosAtivos = (trechos) => (trechos ?? [])
  .filter((t) => !t.arquivado)
  .sort((a, b) => a.titulo.localeCompare(b.titulo, 'pt-BR'))

export const formularioDoTrecho = (trecho) => (trecho
  ? { titulo: trecho.titulo ?? '', texto: trecho.texto ?? '', palavrasChave: trecho.palavrasChave ?? '', nucleo: Boolean(trecho.nucleo) }
  : { ...TRECHO_VAZIO })

export const trechoMudou = (form, trecho) => {
  const original = formularioDoTrecho(trecho)
  return Object.keys(TRECHO_VAZIO).some((campo) => form?.[campo] !== original[campo])
}

// Soma do texto do núcleo ativo como ficaria: os outros trechos mais este, sem contar a versão antiga dele.
export const tamanhoDoNucleo = (trechos, form, idEmEdicao) => trechosAtivos(trechos)
  .filter((t) => t.nucleo && t.id !== idEmEdicao)
  .reduce((soma, t) => soma + t.texto.length, form?.nucleo ? String(form.texto ?? '').trim().length : 0)

export function errosDoTrecho(form, trechos, idEmEdicao) {
  const erros = {}
  const titulo = String(form?.titulo ?? '').trim()
  const texto = String(form?.texto ?? '').trim()
  if (!titulo) erros.titulo = 'Informe o título.'
  else if (titulo.length > LIMITES_DO_CADERNO.titulo) erros.titulo = `Use até ${numero(LIMITES_DO_CADERNO.titulo)} caracteres.`
  if (!texto) erros.texto = 'Escreva o texto do trecho.'
  else if (texto.length > LIMITES_DO_CADERNO.texto) erros.texto = `Use até ${numero(LIMITES_DO_CADERNO.texto)} caracteres.`
  if (normalizarPalavrasChave(form?.palavrasChave).length > LIMITES_DO_CADERNO.palavrasChave) {
    erros.palavrasChave = `Use até ${numero(LIMITES_DO_CADERNO.palavrasChave)} caracteres.`
  }
  const nucleo = tamanhoDoNucleo(trechos, form, idEmEdicao)
  if (form?.nucleo && nucleo > LIMITES_DO_CADERNO.nucleo) {
    erros.nucleo = `O núcleo passaria de ${numero(LIMITES_DO_CADERNO.nucleo)} caracteres (ficaria com ${numero(nucleo)}). `
      + 'Deixe este trecho fora do núcleo: o agente ainda o encontra pelo índice.'
  }
  return erros
}

export const corpoDoTrecho = (form) => ({
  titulo: String(form.titulo ?? '').trim(),
  texto: String(form.texto ?? '').trim(),
  palavrasChave: normalizarPalavrasChave(form.palavrasChave),
  nucleo: Boolean(form.nucleo),
})
