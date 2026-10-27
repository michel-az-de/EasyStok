// Dados da loja (#2070): `PUT api/lojas/{id}` sobrescreve os cinco campos (AtualizarLojaUseCase).
// Por isso o que ela não editou volta exatamente como veio, e só salva quem leu os cinco.
export const CAMPOS_DA_LOJA = ['nome', 'descricao', 'documento', 'endereco', 'telefone']

// Os mesmos limites do comando da API.
export const LIMITES_DA_LOJA = { nome: 150, descricao: 500, documento: 30, endereco: 300, telefone: 20 }

export const lojasAtivas = (lojas) => (lojas ?? []).filter((loja) => loja?.ativa !== false)

// A leitura de hoje (`LojaResult`) só traz id, nome, ativa e logo: sem os outros quatro, salvar apagaria.
export const lojaTemOsCincoCampos = (loja) => Boolean(loja) && CAMPOS_DA_LOJA.every((campo) => Object.hasOwn(loja, campo))

export const formularioDaLoja = (loja) => Object.fromEntries(CAMPOS_DA_LOJA.map((campo) => [campo, loja?.[campo] ?? '']))

export const lojaMudou = (form, loja) => {
  const original = formularioDaLoja(loja)
  return CAMPOS_DA_LOJA.some((campo) => (form?.[campo] ?? '') !== original[campo])
}

export function errosDaLoja(form) {
  const erros = {}
  if (!String(form?.nome ?? '').trim()) erros.nome = 'Informe o nome da loja.'
  for (const campo of CAMPOS_DA_LOJA) {
    if (!erros[campo] && String(form?.[campo] ?? '').trim().length > LIMITES_DA_LOJA[campo]) erros[campo] = `Use até ${LIMITES_DA_LOJA[campo]} caracteres.`
  }
  return erros
}

export function corpoDaLoja(loja, form) {
  const original = formularioDaLoja(loja)
  const valor = (campo) => {
    if ((form?.[campo] ?? '') === original[campo]) return loja[campo] ?? null
    const aparado = String(form[campo] ?? '').trim()
    return aparado || null
  }
  return { lojaId: loja.id, empresaId: loja.empresaId, ...Object.fromEntries(CAMPOS_DA_LOJA.map((campo) => [campo, valor(campo)])) }
}
