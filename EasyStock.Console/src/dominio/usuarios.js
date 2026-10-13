// Usuários e auditoria de acesso (#1753, M7.2). Regras puras: o que a tela mostra de cada pessoa,
// o que a dona pode fazer com ela e como cada linha da auditoria vira frase.

export const SITUACAO = {
  convite: { rotulo: 'Convite enviado', tom: 'aviso' },
  ativo: { rotulo: 'Ativo', tom: 'ok' },
  desativado: { rotulo: 'Desativado', tom: 'neutro' },
}

// Quem foi desativado antes de aceitar o convite é "desativado": o convite já não leva a lugar nenhum.
export const situacaoDoUsuario = (usuario) =>
  !usuario.ativo ? SITUACAO.desativado : usuario.convite?.estado === 'pendente' ? SITUACAO.convite : SITUACAO.ativo

// A API recusa mexer no próprio acesso (USUARIO_PROPRIO); a tela nem oferece.
export function acoesDoUsuario(usuario, meuId) {
  const eu = usuario.usuarioId === meuId
  const pendente = usuario.convite?.estado === 'pendente'
  return {
    trocarPerfil: !eu && usuario.ativo,
    reenviarConvite: !eu && usuario.ativo && pendente,
    desativar: !eu && usuario.ativo,
    reativar: !eu && !usuario.ativo,
  }
}

// Os perfis da casa primeiro, na ordem em que a dona pensa neles; os demais (legado) depois, por nome.
const ORDEM_DA_CASA = ['dona', 'atendimento', 'cozinha']
const chave = (nome) => String(nome ?? '').trim().toLocaleLowerCase('pt-BR')
export function ordenarPerfis(perfis) {
  const posicao = (p) => { const i = ORDEM_DA_CASA.indexOf(chave(p.nome)); return i === -1 ? ORDEM_DA_CASA.length : i }
  return [...perfis].sort((a, b) => posicao(a) - posicao(b) || a.nome.localeCompare(b.nome, 'pt-BR'))
}

const RESUMO_DO_PERFIL = {
  dona: 'Vê e altera tudo, inclusive usuários',
  atendimento: 'Conversas, pedidos, cardápio, caixa e entregas',
  cozinha: 'Fila de preparo e produção',
}
export const resumoDoPerfil = (nome) => RESUMO_DO_PERFIL[chave(nome)] ?? null

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
export function erroDoConvite({ nome, email, perfilId }) {
  if (!nome.trim()) return 'Diga o nome da pessoa.'
  if (!EMAIL.test(email.trim())) return 'Confira o e-mail: é para ele que o convite vai.'
  if (!perfilId) return 'Escolha o perfil.'
  return null
}

// Política de senha da API (SenhaRules): a tela mostra o que falta antes de enviar.
export const REGRAS_DE_SENHA = [
  { id: 'tamanho', rotulo: '10 caracteres ou mais', vale: (s) => s.length >= 10 },
  { id: 'maiuscula', rotulo: 'uma letra maiúscula', vale: (s) => /[A-Z]/.test(s) },
  { id: 'minuscula', rotulo: 'uma letra minúscula', vale: (s) => /[a-z]/.test(s) },
  { id: 'numero', rotulo: 'um número', vale: (s) => /[0-9]/.test(s) },
  { id: 'especial', rotulo: 'um símbolo, como ! ou @', vale: (s) => /\W/.test(s) },
]
export const regrasDaSenha = (senha) => REGRAS_DE_SENHA.map((r) => ({ id: r.id, rotulo: r.rotulo, ok: r.vale(senha) }))
export const senhaValida = (senha) => REGRAS_DE_SENHA.every((r) => r.vale(senha))

// O token do convite chega no fragmento (`#/convite?t=...`), fora do log de acesso do servidor.
export function tokenDoConvite(hash) {
  const achado = /^#\/convite\?(.*)$/.exec(hash ?? '')
  if (!achado) return null
  const token = new URLSearchParams(achado[1]).get('t')
  return token && token.trim() ? token.trim() : null
}
export const ehRotaDeConvite = (hash) => /^#\/convite(\?|$)/.test(hash ?? '')

// Auditoria: a ação gravada pela API vira o que aconteceu, em palavras da casa.
const ACOES = {
  login: 'Entrou',
  login_novo_dispositivo: 'Entrou em outro aparelho',
  logout: 'Saiu',
  'usuario-convidado': 'Convite enviado',
  'usuario-criado': 'Acesso criado',
  'aceitar-convite': 'Aceitou o convite',
  'convite-reenviado': 'Convite reenviado',
  'perfil-trocado': 'Perfil trocado',
  'usuario-desativado': 'Acesso desativado',
  'usuario-reativado': 'Acesso reativado',
}
// Texto de máquina que não acrescenta nada à linha: a ação já diz o que houve.
const SEM_INFORMACAO = [/^Login realizado com sucesso$/, /^Login detectado em novo dispositivo/]
// Linhas antigas guardaram o nível e o canal em jargão: "(Admin); WhatsApp: nao".
const limpar = (texto) => texto
  .replace(/\s*\((Admin|SuperAdmin|Gerente|Operador)\)/g, '')
  .replace(/;\s*WhatsApp:\s*sim/i, ', também pelo WhatsApp')
  .replace(/;\s*WhatsApp:\s*n[aã]o/i, '')
  .trim()

export function linhaDeAuditoria(item) {
  const conhecido = ACOES[item.acao]
  const detalhe = item.detalhes && !SEM_INFORMACAO.some((r) => r.test(item.detalhes)) ? limpar(item.detalhes) : null
  return {
    id: item.id,
    quando: item.dataHora,
    pessoa: item.usuarioNome,
    usuarioId: item.usuarioId,
    acao: item.acao,
    // O detalhe já é a frase inteira ("Perfil trocado de Cozinha para Atendimento por Baba"); sem ele, vale a ação.
    oQue: detalhe ?? conhecido ?? item.acao.replaceAll(/[-_]/g, ' '),
    falhou: item.sucesso === false,
    // Entrar e sair são rotina; o resto muda quem pode o quê.
    rotina: item.acao === 'login' || item.acao === 'logout' || item.acao === 'login_novo_dispositivo',
  }
}

// A API grava duas linhas quando alguém entra em outro aparelho (a entrada e o aviso): fica só o aviso.
// Entradas seguidas da mesma pessoa viram uma linha com a contagem; a lista chega da mais recente para a mais antiga.
const ENTRADA = new Set(['login', 'login_novo_dispositivo'])
const instante = (texto) => Date.parse(/[zZ]|[+-]\d\d:?\d\d$/.test(texto) ? texto : `${texto}Z`)
export function condensarAuditoria(linhas, mesmoDia = () => true) {
  const semGemeas = linhas.filter((linha, i) => {
    if (linha.acao !== 'login') return true
    return [linhas[i - 1], linhas[i + 1]].every((v) => !v || v.acao !== 'login_novo_dispositivo'
      || v.usuarioId !== linha.usuarioId || Math.abs(instante(v.quando) - instante(linha.quando)) >= 5000)
  })
  const saida = []
  for (const linha of semGemeas) {
    const anterior = saida.at(-1)
    if (anterior && ENTRADA.has(linha.acao) && ENTRADA.has(anterior.acao) && anterior.usuarioId === linha.usuarioId
      && !linha.falhou && !anterior.falhou && mesmoDia(anterior.quando, linha.quando)) {
      anterior.vezes = (anterior.vezes ?? 1) + 1
      anterior.primeira = linha.quando
      anterior.oQue = `Entrou ${anterior.vezes} vezes`
      continue
    }
    saida.push({ ...linha })
  }
  return saida
}

// A API devolve UTC sem o "Z" em alguns campos; sem ele o navegador leria como hora local.
export const instanteDaApi = (texto) => {
  if (!texto) return null
  const ms = Date.parse(/[zZ]|[+-]\d\d:?\d\d$/.test(texto) ? texto : `${texto}Z`)
  return Number.isNaN(ms) ? null : ms
}

// Agrupa por dia de calendário já formatado (a formatação no fuso é de quem chama).
export function agruparPorDia(linhas, diaDe) {
  const grupos = []
  for (const linha of linhas) {
    const dia = diaDe(linha.quando)
    const ultimo = grupos.at(-1)
    if (ultimo && ultimo.dia === dia) ultimo.linhas.push(linha)
    else grupos.push({ dia, linhas: [linha] })
  }
  return grupos
}
