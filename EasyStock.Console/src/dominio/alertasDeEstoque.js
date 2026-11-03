// Alertas de estoque (#2070, DM7-1): parte da `ConfiguracaoLoja` (`api/configuracoes`) que a dona usa
// no dia. Moeda, fuso, KDS e contas automáticas ficam de fora. O PATCH trata nulo como "não muda",
// então o corpo leva só o que ela mudou: outro aparelho pode ter mexido no resto.
export const CAMPOS_DOS_ALERTAS = ['diasAlertaValidade', 'diasAlertaParado', 'quantidadeMinimaPadrao', 'quantidadeCriticaPadrao']

// Os mesmos limites da tela do Web (`_ConfiguracoesGeralTab.cshtml`).
export const LIMITES_DOS_ALERTAS = {
  diasAlertaValidade: [1, 365], diasAlertaParado: [1, 365], quantidadeMinimaPadrao: [0, 9999], quantidadeCriticaPadrao: [0, 9999],
}

// `FifoAtivo` verdadeiro sai pelo lote que vence antes (FEFO); falso, pelo que entrou antes.
export const ORDENS_DE_SAIDA = [
  { valor: 'fefo', rotulo: 'Primeiro o que vence antes (recomendado)' },
  { valor: 'fifo', rotulo: 'Primeiro o que entrou antes' },
]

export const formularioDosAlertas = (config) => ({
  ...Object.fromEntries(CAMPOS_DOS_ALERTAS.map((campo) => [campo, String(config?.[campo] ?? '')])),
  ordemDeSaida: config?.fifoAtivo === false ? 'fifo' : 'fefo',
})

const inteiro = (texto) => (/^\d+$/.test(String(texto ?? '').trim()) ? Number(texto) : null)

export const alertasMudaram = (form, config) => {
  const original = formularioDosAlertas(config)
  return [...CAMPOS_DOS_ALERTAS, 'ordemDeSaida'].some((campo) => String(form?.[campo] ?? '').trim() !== original[campo])
}

export function errosDosAlertas(form) {
  const erros = {}
  for (const campo of CAMPOS_DOS_ALERTAS) {
    const [minimo, maximo] = LIMITES_DOS_ALERTAS[campo]
    const valor = inteiro(form?.[campo])
    if (valor === null || valor < minimo || valor > maximo) erros[campo] = `Use um número inteiro de ${minimo} a ${maximo}.`
  }
  const minima = inteiro(form?.quantidadeMinimaPadrao)
  const critica = inteiro(form?.quantidadeCriticaPadrao)
  if (!erros.quantidadeMinimaPadrao && !erros.quantidadeCriticaPadrao && critica >= minima) {
    erros.quantidadeCriticaPadrao = 'O nível crítico deve ser menor que o mínimo.'
  }
  return erros
}

export function corpoDosAlertas(config, form, { empresaId, lojaId }) {
  const original = formularioDosAlertas(config)
  const corpo = { empresaId, lojaId }
  for (const campo of CAMPOS_DOS_ALERTAS) {
    if (String(form[campo]).trim() !== original[campo]) corpo[campo] = inteiro(form[campo])
  }
  if (form.ordemDeSaida !== original.ordemDeSaida) corpo.fifoAtivo = form.ordemDeSaida === 'fefo'
  return corpo
}
