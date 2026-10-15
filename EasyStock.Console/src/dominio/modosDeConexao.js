// Como a casa fala com o modelo. É vocabulário de produto: a dona precisa ler
// e entender. O transporte de verdade mora em infra e não aparece na tela.

export const MODOS = {
  simulado: {
    chave: 'simulado',
    rotulo: 'Simulado no navegador',
    detalhe: 'Protótipo. Nenhuma chamada sai da máquina.',
    tom: 'aviso',
  },
  cli: {
    chave: 'cli',
    rotulo: 'Claude pela linha de comando',
    detalhe: 'Como está sendo usado hoje, fora do sistema.',
    tom: 'info',
  },
  api: {
    chave: 'api',
    rotulo: 'Claude pela API',
    detalhe: 'Destino no sistema: o backend chama a API e devolve para esta tela.',
    tom: 'ok',
  },
}

export const MODELO_PREVISTO = 'claude-sonnet-5'

export const listaDeModos = () => Object.values(MODOS)
