import { gerarCodigoDePareamento, listarAparelhos, listarLojas } from '../infra/api/aparelhosApi'
import { lerSessao } from '../infra/api/sessao'

// #2081 (A6): o APK piloto diz "Solicite o código de pareamento à equipe". Aqui a dona ou a gerente
// gera o código, que vale 10 minutos. O código fica só na memória do modal: não vai para URL, log
// nem armazenamento. Separado do componente para a prova rodar sem React.

// As lojas onde o aparelho pode ficar. A lista de lojas é de Configurações (a dona lê); para quem
// não tem o módulo, servem as lojas dos aparelhos já pareados. Sem nenhuma das duas, não há como
// saber a loja e o erro diz isso.
export async function lojasParaParear() {
  const empresaId = lerSessao()?.empresa?.id
  if (!empresaId) throw new Error('Entre novamente na empresa para continuar.')
  try {
    const lojas = (await listarLojas()) ?? []
    const ativas = lojas.filter((l) => l.ativa !== false).map((l) => ({ id: l.id, nome: l.nome ?? 'Loja' }))
    if (ativas.length > 0) return { empresaId, lojas: ativas }
  } catch (erro) {
    if (erro?.status !== 403) throw erro
  }
  const aparelhos = (await listarAparelhos(empresaId)) ?? []
  const ids = [...new Set(aparelhos.filter((a) => !a.revoked && a.lojaId).map((a) => a.lojaId))]
  if (ids.length === 0) {
    throw new Error('Não consegui saber a loja do aparelho. Peça a quem administra a empresa para gerar o código.')
  }
  return { empresaId, lojas: ids.map((id, i) => ({ id, nome: ids.length === 1 ? 'Loja dos aparelhos' : `Loja ${i + 1} dos aparelhos` })) }
}

export async function gerarCodigo({ empresaId, lojaId }) {
  if (!lojaId) throw new Error('Escolha a loja do aparelho.')
  const r = await gerarCodigoDePareamento({ empresaId, lojaId })
  const codigo = String(r?.pairingCode ?? '')
  if (!/^\d{6}$/.test(codigo)) throw new Error('O EasyStok não devolveu o código. Tente de novo.')
  return { codigo, expiraEm: r.expiresAt ?? null }
}

// "9:58" até o código vencer; null quando já venceu (ou sem prazo).
export function tempoRestante(expiraEm, agora = Date.now()) {
  const fim = expiraEm ? Date.parse(expiraEm) : NaN
  if (!Number.isFinite(fim)) return null
  const segundos = Math.floor((fim - agora) / 1000)
  if (segundos <= 0) return null
  return `${Math.floor(segundos / 60)}:${String(segundos % 60).padStart(2, '0')}`
}

// #2235 (banca P2-2): o botão diz qual aparelho está ligado e quando apareceu pela última vez.
// Conta só o pareado e não revogado; o código gerado e ainda não usado não é aparelho.
export function estadoDosAparelhos(aparelhos = [], agora = Date.now()) {
  const ativos = aparelhos.filter((a) => !a.revoked && !a.pendingPair)
  if (ativos.length === 0) return 'Nenhum aparelho conectado'
  if (ativos.length > 1) return `${ativos.length} aparelhos conectados`
  const [a] = ativos
  // O pareamento automático grava o rótulo técnico "auto-provisioned": não é nome de aparelho.
  const rotulo = a.label?.trim() === 'auto-provisioned' ? '' : a.label?.trim()
  const nome = rotulo || a.defaultOperatorName?.trim() || 'Aparelho'
  return `${nome} · ${vistoEm(a.lastSeenAt, agora)}`
}

function vistoEm(iso, agora) {
  const visto = iso ? Date.parse(iso) : NaN
  if (!Number.isFinite(visto)) return 'conectado'
  const minutos = Math.max(0, Math.floor((agora - visto) / 60000))
  if (minutos < 15) return 'ativo agora'
  if (minutos < 60) return `visto há ${minutos} min`
  const horas = Math.floor(minutos / 60)
  if (horas < 24) return `visto há ${horas} h`
  return `visto em ${new Date(visto).toLocaleDateString('pt-BR', { timeZone: 'America/Sao_Paulo', day: '2-digit', month: '2-digit' })}`
}

export async function lerEstadoDosAparelhos() {
  const empresaId = lerSessao()?.empresa?.id
  if (!empresaId) return null
  return estadoDosAparelhos((await listarAparelhos(empresaId)) ?? [])
}

// "123 456": dois grupos de três, mais fácil de ditar e de digitar no celular.
export const codigoEmGrupos = (codigo) => `${codigo.slice(0, 3)} ${codigo.slice(3)}`
