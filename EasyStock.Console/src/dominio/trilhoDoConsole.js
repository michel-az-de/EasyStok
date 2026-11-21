// Itens do trilho lateral do Console (#3004). Uma definição só, pura, para o Início, o Balcão e os
// módulos: a mesma lista e a mesma ordem em toda rota. O que só funciona no Balcão (estado da loja,
// Gestão, Lembretes, Disposição, Simular) vira link para o Balcão nas outras telas, com o nome acessível
// "<rótulo>, abre no Balcão", em vez de sumir ou ficar parado: quem usa acha cada coisa sempre no mesmo lugar.
//
// `onde`: 'hall' (Início), 'balcao' ou { moduloId, telaId } da rota do módulo.
// Ajuda, Tema e Sair não são itens: moram no menu da conta, no pé do trilho.
import {
  MODULOS, NOME_INICIO, NOME_RESPOSTAS_AUTOMATICAS, atalhosDiretos, ehAtalhoDireto, emOrdemDosModulos, hashDoModulo,
  moduloPorId, telasDoMenu,
} from './modulos'

export const HASH_INICIO = '#/'
export const HASH_BALCAO = '#/m/atendimento/balcao'
// Teto de itens do trilho (medido: 16 em modo API, 17 com Simular na demonstração); as provas 3004 e 1839 usam o mesmo número.
export const TETO_ITENS_DO_TRILHO = 17

// Em módulos o rail abre as telas por link; só estes dependem do Balcão aberto.
const SO_NO_BALCAO = [
  { id: 'loja', rotulo: 'Loja' },
  { id: 'gestao', rotulo: 'Gestão' },
  { id: 'lembretes', rotulo: 'Lembretes' },
  { id: 'disposicao', rotulo: 'Disposição' },
]
// "Configurações" mede ~77 px em 12 px sem espaçamento negativo e o item do trilho tem 72: o rótulo do trilho é
// "Ajustes"; o nome acessível e o `title` seguem o nome inteiro do módulo.
const ROTULO_CURTO = { configuracoes: 'Ajustes' }
const SIMULAR = { id: 'simular', rotulo: 'Simular' }

const doBalcao = (onde) => onde === 'balcao'

export function itensDoTrilho({ permite = () => true, acoes = null, fonteApi = false, simulacoes = false, onde = 'hall' } = {}) {
  const rota = typeof onde === 'object' && onde ? onde : { moduloId: null, telaId: null }
  const naRespostas = rota.moduloId === 'atendimento' && rota.telaId === 'respostas'
  const noAtalho = naRespostas || ehAtalhoDireto(rota.moduloId, rota.telaId)
  const areaAtual = (id) => (doBalcao(onde) ? id === 'atendimento' : id === rota.moduloId && !noAtalho)
  const soNoBalcao = ({ id, rotulo }) => {
    const fora = !doBalcao(onde)
    return { id, rotulo, nome: fora ? `${rotulo}, abre no Balcão` : rotulo, soNoBalcao: fora, href: fora ? HASH_BALCAO : undefined, atual: false }
  }
  const areas = emOrdemDosModulos(MODULOS)
    .filter((modulo) => permite(modulo.id) && telasDoMenu(modulo, { fonteApi, acoes }).length)
    .map((modulo) => ({ id: modulo.id, rotulo: ROTULO_CURTO[modulo.id] ?? modulo.nome, nome: modulo.nome, modulo, href: hashDoModulo(modulo.id), atual: areaAtual(modulo.id) }))
  const atalhos = atalhosDiretos({ fonteApi, acoes, permite }).map((atalho) => ({
    id: atalho.id, rotulo: atalho.nome, nome: atalho.nome, modulo: atalho.modulo, telaId: atalho.telaId, href: atalho.href,
    atual: atalho.moduloId === rota.moduloId && atalho.telaId === rota.telaId,
  }))
  const respostas = permite('atendimento') ? [{
    id: 'respostas', rotulo: NOME_RESPOSTAS_AUTOMATICAS, nome: NOME_RESPOSTAS_AUTOMATICAS, modulo: moduloPorId('atendimento'),
    telaId: 'respostas', href: hashDoModulo('atendimento', 'respostas'), atual: naRespostas,
  }] : []
  const [loja, gestao, lembretes, disposicao] = SO_NO_BALCAO.map(soNoBalcao)
  return [
    { id: 'inicio', rotulo: NOME_INICIO, nome: NOME_INICIO, href: HASH_INICIO, atual: onde === 'hall' },
    loja,
    ...areas,
    ...atalhos,
    ...respostas,
    gestao,
    lembretes,
    disposicao,
    ...(simulacoes ? [soNoBalcao(SIMULAR)] : []),
  ]
}
