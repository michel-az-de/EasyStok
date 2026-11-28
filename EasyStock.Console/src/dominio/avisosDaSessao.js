// #3394: regras puras do painel de Avisos junto ao sino. O dado chega por parâmetro; quem mede a tela e
// quem chama a API ficam em `features/notificacoes` e `aplicacao/useNotificacoesDaSessao.js`.

import { podeVerDesacertos } from './acessoModulos'
import { hashDoModulo, moduloPorId } from './modulos'
import { ROTA_MODULO, rotaDaHash } from './rota'

const GRUPOS = ['Hoje', 'Ontem', 'Anteriores']

// #3402: como nas redes, tocar no aviso abre a coisa avisada. `tipo` é o nome de TipoAlertaEstoque que a API manda
// (NotificacaoController: `TipoAlerta.ToString()`), com os três apelidos do enum, porque o banco guarda o texto e
// Enum.ToString não garante qual nome sai. O Console não tem rota por item: `referenciaId` (o produto ou o lote, no
// GeradorNotificacoesAutomaticas) não entra no destino, que é a tela onde a coisa se resolve.
//   Estoque do dia: abaixo do mínimo, reposição, validade perto e lote parado.
//   Perdas: o lote vencido com saldo, que a própria tela lista para lançar a perda.
// Sem tela no Console, o aviso fica só com a leitura: pedido ao fornecedor (PedidoAtrasado, PedidoRecebido, este
// também do canal in-app, com o id do outbox) e AvisoOperacional (aparelho, prazo, caixa, produto vencendo da rotina).
const ESTOQUE_DO_DIA = ['producao', 'estoque', 'no']
const PERDAS = ['producao', 'perdas', 'em']
const TELA_DO_TIPO = {
  EstoqueCritico: ESTOQUE_DO_DIA, EstoqueBaixo: ESTOQUE_DO_DIA,
  ReposicaoSugerida: ESTOQUE_DO_DIA, Reposicao: ESTOQUE_DO_DIA,
  ValidadeProxima: ESTOQUE_DO_DIA, ProximoVencimento: ESTOQUE_DO_DIA,
  ProdutoParado: ESTOQUE_DO_DIA,
  ProdutoVencido: PERDAS,
}

// Para onde o aviso leva: `{ hash, tela, chamada }` ou null; a chamada ("Ver no Estoque do dia") diz a tela, não
// promete o item. Só vira link o que o perfil abre: o módulo liberado e a permissão
// de estoque, que a matriz diz com `verDesacertosEstoque` (as duas telas pedem GerenciarEstoque na API). A rota passa
// pelo mesmo `rotaDaHash` do app: tela congelada ou fora do catálogo cai no hall e deixa o aviso sem link.
export function destinoDoAviso(aviso, { permite, acoes } = {}) {
  const alvo = Object.hasOwn(TELA_DO_TIPO, aviso?.tipo ?? '') ? TELA_DO_TIPO[aviso.tipo] : null
  if (!alvo || typeof permite !== 'function') return null
  const [moduloId, telaId, preposicao] = alvo
  if (!permite(moduloId) || !podeVerDesacertos(acoes)) return null
  const hash = hashDoModulo(moduloId, telaId)
  const rota = rotaDaHash(hash, { fonteApi: true })
  if (rota.tipo !== ROTA_MODULO || rota.tela !== telaId) return null
  const tela = moduloPorId(moduloId).telas.find((t) => t.id === telaId).rotulo
  return { hash, tela, chamada: `Ver ${preposicao} ${tela}` }
}

const inicioDoDia = (instante) => {
  const dia = new Date(instante)
  dia.setHours(0, 0, 0, 0)
  return dia.getTime()
}

// Como no Instagram e no Facebook, a lista se lê por dia. A ordem que a API mandou (mais novo primeiro) fica
// dentro de cada grupo, e grupo vazio não aparece. Data que não se lê vai para Anteriores, nunca para Hoje.
export function agruparAvisos(avisos, agora) {
  const hoje = inicioDoDia(agora)
  const ontem = new Date(hoje)
  ontem.setDate(ontem.getDate() - 1)
  const grupoDe = (aviso) => {
    const instante = aviso.createdAt ? new Date(aviso.createdAt).getTime() : NaN
    if (Number.isNaN(instante)) return 'Anteriores'
    if (instante >= hoje) return 'Hoje'
    return instante >= ontem.getTime() ? 'Ontem' : 'Anteriores'
  }
  const porGrupo = new Map(GRUPOS.map((titulo) => [titulo, []]))
  for (const aviso of avisos) porGrupo.get(grupoDe(aviso)).push(aviso)
  return GRUPOS.map((titulo) => ({ titulo, avisos: porGrupo.get(titulo) })).filter((grupo) => grupo.avisos.length > 0)
}

const VAO = 8 // distância entre o sino e o painel
const MARGEM = 12 // distância mínima entre o painel e a borda da janela
const LARGURA = 400
const TETO = 640 // altura máxima do painel; numa tela alta ele não vira uma coluna inteira
const CELULAR = 760

// Onde o painel abre, em coordenadas da janela. Três lugares, um por casa do sino:
//   folha   (celular): largura da tela, logo abaixo do sino do topo;
//   direita (trilho do desktop): ao lado do trilho, à altura do sino, sem passar do pé (o Instagram abre
//           as notificações ao lado da barra lateral);
//   abaixo  (topo do tablet e do hall): embaixo do sino, alinhado pela direita dele (o Facebook faz assim),
//           sem sair da janela.
export function posicaoDoPainel({ ancora, janela, alturaDoPainel }) {
  if (janela.largura < CELULAR) {
    const top = ancora.bottom + VAO
    return { lado: 'folha', top, left: VAO, right: VAO, maxHeight: Math.max(1, Math.min(TETO, janela.altura - top - VAO)) }
  }
  const largura = Math.min(LARGURA, janela.largura - 2 * MARGEM)
  const maxHeight = Math.min(TETO, janela.altura - 2 * MARGEM)
  const noTrilho = (ancora.left + ancora.right) / 2 < janela.largura / 3 && ancora.bottom > janela.altura / 4
  if (noTrilho) {
    const altura = Math.min(alturaDoPainel, maxHeight)
    const top = Math.max(MARGEM, Math.min(ancora.top, janela.altura - MARGEM - altura))
    return { lado: 'direita', top, left: ancora.right + VAO, width: largura, maxHeight: Math.min(TETO, janela.altura - MARGEM - top) }
  }
  // Cresce para a esquerda a partir da borda direita do sino; se assim sairia da tela (sino no começo do topo,
  // como no tablet), encosta na margem, como o Popover faz.
  const top = ancora.bottom + VAO
  const left = Math.max(MARGEM, Math.min(ancora.right - largura, janela.largura - largura - MARGEM))
  return { lado: 'abaixo', top, left, width: largura, maxHeight: Math.max(1, Math.min(TETO, janela.altura - top - MARGEM)) }
}
