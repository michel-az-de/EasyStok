import { chamarApi } from './cliente'

// Configuração do atendimento (S08): `api/atendimento/configuracao`. Sem registro, a API
// devolve o padrão; nunca 404. Campo omitido no PUT mantém o valor atual.
const BASE = '/api/atendimento/configuracao'

export const obterConfiguracao = () => chamarApi(BASE)

export const salvarConfiguracao = (c) => chamarApi(BASE, {
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
    ativo: c.ativo,
  },
})
