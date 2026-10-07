import { chamarApi } from './cliente'

// Configuração do atendimento (S08): `api/atendimento/configuracao`. Sem registro, a API
// devolve o padrão; nunca 404. Campo omitido no PUT mantém o valor atual.
const BASE = '/api/atendimento/configuracao'

export const obterConfiguracao = () => chamarApi(BASE)

// S58 (#1391): o modelo de retomada vai num PUT próprio; a resposta dele é a configuração inteira gravada.
export const salvarConfiguracao = async (c) => {
  await salvarGeral(c)
  return chamarApi(`${BASE}/modelo-retomada`, {
    metodo: 'PUT',
    corpo: { nome: c.modeloRetomadaNome?.trim() || null, idioma: c.modeloRetomadaIdioma || null },
  })
}

const salvarGeral = (c) => chamarApi(BASE, {
  metodo: 'PUT',
  corpo: {
    tom: c.tom,
    nivelSugestao: c.nivelSugestao,
    saudacaoPrimeiroContato: c.saudacaoPrimeiroContato,
    saudacaoRetorno: c.saudacaoRetorno,
    fraseEspera: c.fraseEspera,
    mensagemForaArea: c.mensagemForaArea,
    respiroMinutos: Number(c.respiroMinutos),
    tempoPreparoPadraoMinutos: Number(c.tempoPreparoPadraoMinutos),
    // #1427: SLA de primeira resposta da loja (1..240); vazio mantém o atual.
    slaRespostaMinutos: c.slaRespostaMinutos == null || c.slaRespostaMinutos === '' ? null : Number(c.slaRespostaMinutos),
    ativo: c.ativo,
  },
})
