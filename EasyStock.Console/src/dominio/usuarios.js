// Usuários e auditoria de acesso (#1753, M7.2). Regras puras: o que a tela mostra de cada pessoa,
// o que quem está na tela pode fazer com ela e como cada linha da auditoria vira frase.
// #2070: o que cada um pode fazer vem da política de hierarquia da API (`acoes` de cada linha); aqui só se lê.

export const SITUACAO = {
  convite: { rotulo: 'Convite enviado', tom: 'aviso' },
  ativo: { rotulo: 'Ativo', tom: 'ok' },
  desativado: { rotulo: 'Desativado', tom: 'neutro' },
}

// Quem foi desativado antes de aceitar o convite é "desativado": o convite já não leva a lugar nenhum.
export const situacaoDoUsuario = (usuario) =>
  !usuario.ativo ? SITUACAO.desativado : usuario.convite?.estado === 'pendente' ? SITUACAO.convite : SITUACAO.ativo

// As ações que a API devolve em cada linha (`GET api/usuarios`), calculadas pela política de hierarquia.
export const ACOES_DA_LINHA = ['editarNome', 'trocarEmail', 'trocarPerfil', 'desativar', 'reativar', 'reenviarConvite', 'marcarDona', 'desmarcarDona']
// Os controles que a linha tem, na ordem da tela. Editar nome e trocar e-mail ainda não têm formulário no console.
export const CONTROLES_DA_LINHA = ['trocarPerfil', 'reenviarConvite', 'marcarDona', 'desmarcarDona', 'desativar', 'reativar']

// Só `true` liga: resposta sem `acoes` (antiga ou incompleta) não oferece nada, e a API recusa o resto.
export const acoesDoUsuario = (usuario) => Object.fromEntries(ACOES_DA_LINHA.map((acao) => [acao, usuario?.acoes?.[acao] === true]))
export function controlesDaPessoa(usuario) {
  const pode = acoesDoUsuario(usuario)
  return CONTROLES_DA_LINHA.filter((controle) => pode[controle])
}

// "Dona" é a marca do vínculo, não um perfil; a sessão do suporte é a de nível SuperAdmin.
export const ehDona = (usuario) => usuario?.dona === true
export const ehSuporteDaSessao = (sessao) => sessao?.usuario?.nivel === 'SuperAdmin'
const primeiroNome = (nome) => String(nome ?? '').trim().split(/\s+/)[0]

// Os perfis da casa primeiro, na ordem em que a dona pensa neles; os demais (legado) depois, por nome.
const ORDEM_DA_CASA = ['administracao', 'gerencia', 'atendimento', 'cozinha']
const chave = (nome) => String(nome ?? '').trim().toLocaleLowerCase('pt-BR')
// O perfil escrito sem acento cai no mesmo lugar.
const chaveDoPerfil = (nome) => chave(nome).normalize('NFD').replace(/\p{M}/gu, '')
export function ordenarPerfis(perfis) {
  const posicao = (p) => { const i = ORDEM_DA_CASA.indexOf(chaveDoPerfil(p.nome)); return i === -1 ? ORDEM_DA_CASA.length : i }
  return [...perfis].sort((a, b) => posicao(a) - posicao(b) || a.nome.localeCompare(b.nome, 'pt-BR'))
}

const RESUMO_DO_PERFIL = {
  administracao: 'Vê e altera tudo, inclusive Configurações e a equipe',
  gerencia: 'Toda a operação, com estoque e relatórios, menos Configurações',
  atendimento: 'Conversas, pedidos, cardápio, caixa e entregas',
  cozinha: 'Fila de preparo e produção',
}
export const resumoDoPerfil = (nome) => RESUMO_DO_PERFIL[chaveDoPerfil(nome)] ?? null

// A API diz quais perfis quem está na tela pode dar (Administração não dá perfil de nível Admin).
export const perfisAtribuiveis = (perfis) => perfis.filter((p) => p.atribuivel === true)
const opcaoDoPerfil = (p) => ({ valor: p.perfilId, rotulo: p.nome })
// O perfil atual sempre aparece, mesmo que não esteja entre os que se pode dar.
export function opcoesDoPerfilDaPessoa(usuario, perfis) {
  const opcoes = perfisAtribuiveis(perfis).map(opcaoDoPerfil)
  if (!usuario.perfilId) return [{ valor: '', rotulo: 'Sem perfil' }, ...opcoes]
  return opcoes.some((o) => o.valor === usuario.perfilId) ? opcoes : [{ valor: usuario.perfilId, rotulo: usuario.perfil }, ...opcoes]
}

// A dona é convidada com perfil de nível Admin que quem convida pode dar; o resto a API confere (403 HIERARQUIA).
export const podeConvidarComoDona = (perfil) => perfil?.atribuivel === true && perfil?.nivel === 'Admin'
export const DICA_DO_EMAIL_DO_CONVITE = 'O convite chega neste e-mail. Pelo link, a pessoa entra com a conta Google deste e-mail ou cria uma senha.'
export const CONVITE_VAZIO = { nome: '', email: '', emailDeNovo: '', perfilId: '', dona: false }

// O que o formulário do convite mostra a partir do que está escrito. Trocar para perfil sem a opção tira a marca de dona.
export function estadoDoConvite(campos, perfis) {
  const atribuiveis = perfisAtribuiveis(perfis)
  const perfil = atribuiveis.find((p) => p.perfilId === campos.perfilId) ?? null
  const ofereceDona = podeConvidarComoDona(perfil)
  const comoDona = ofereceDona && campos.dona === true
  return {
    opcoes: [{ valor: '', rotulo: 'Escolha o perfil' }, ...atribuiveis.map(opcaoDoPerfil)],
    resumo: resumoDoPerfil(perfil?.nome),
    ofereceDona,
    comoDona,
    pedeEmailDeNovo: comoDona,
  }
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
const chaveDoEmail = (email) => String(email ?? '').trim().toLowerCase()
// `dona` aqui já é a marca que vale (a de `estadoDoConvite`): só ela pede o e-mail duas vezes.
export function erroDoConvite({ nome, email, perfilId, dona = false, emailDeNovo = '' }) {
  if (!nome.trim()) return 'Escreva o nome da pessoa.'
  if (!EMAIL.test(email.trim())) return 'Confira o e-mail: o convite vai para ele.'
  if (!perfilId) return 'Escolha o perfil.'
  if (dona && !String(emailDeNovo ?? '').trim()) return 'Digite o e-mail mais uma vez para conferir.'
  if (dona && chaveDoEmail(emailDeNovo) !== chaveDoEmail(email)) return 'Os dois e-mails não são iguais. Confira e digite o mesmo nas duas vezes.'
  return null
}

// Marcar e tirar a marca de dona pedem confirmação; o suporte explica o porquê (fica no histórico da API).
export const MINIMO_DO_MOTIVO = 10
export const erroDoMotivo = (motivo) => (String(motivo ?? '').trim().length >= MINIMO_DO_MOTIVO
  ? null
  : `Escreva o motivo com pelo menos ${MINIMO_DO_MOTIVO} caracteres. Ele fica no histórico.`)
export function confirmacaoDaMarcaDeDona(acao, usuario, suporte) {
  const nome = primeiroNome(usuario.nome)
  if (acao === 'marcarDona') {
    return {
      texto: `Como dona, ${nome} pode tudo, inclusive marcar outras donas. Para trocar o perfil ou desativar uma dona, antes é preciso tirar a marca.`,
      confirmar: `Marcar ${nome} como dona`,
      pedeMotivo: suporte === true,
    }
  }
  return {
    texto: usuario.perfil ? `${nome} continua com o perfil ${usuario.perfil}, mas deixa de ser dona.` : `${nome} deixa de ser dona.`,
    confirmar: `Tirar a marca de ${nome}`,
    pedeMotivo: suporte === true,
  }
}

// Recusas da política de hierarquia (#2070), na língua da casa. Código que a tela não conhece mostra a frase da API.
export const MENSAGEM_DO_CODIGO = {
  HIERARQUIA: 'O seu perfil não permite esta mudança. Peça à dona da casa.',
  USUARIO_PROPRIO: 'Você não pode mudar o seu próprio acesso.',
  DONA_PROTEGIDA: 'Ninguém da casa troca o perfil nem desativa uma dona. Antes é preciso tirar a marca de dona.',
  DONA_EMAIL_EXISTENTE: 'Este e-mail já tem conta no sistema. Convide a dona com um e-mail que ainda não tenha conta.',
  ULTIMA_DONA: 'A casa precisa de pelo menos uma dona. Marque outra pessoa como dona antes.',
  DONA_NAO_ADMIN: 'Só quem tem o perfil Administração pode ser dona. Troque o perfil antes.',
  DONA_CONTA_NAO_PROVADA: 'Esta pessoa ainda não confirmou a conta. Ela precisa aceitar o convite pelo e-mail ou entrar com a conta Google antes.',
  DONA_SUPORTE: 'O acesso do suporte não pode ser marcado como dona.',
}
export const mensagemDaFalha = (falha) => MENSAGEM_DO_CODIGO[falha?.codigo] ?? falha?.message

// O que a dona lê depois de cada ação. Quem nunca entrou não tem sessão para cair, então a frase não fala em entrar de novo.
const FEITO = {
  convite: (u) => `${u.dona ? 'Convite de dona enviado' : 'Convite enviado'} para ${u.email}. Chega em até um minuto.`,
  reenviar: (u) => `Convite reenviado para ${u.email}. O link anterior deixou de valer.`,
  perfil: (u, perfil) => (u.ultimoAcessoEm
    ? `${u.nome} agora tem o perfil ${perfil}. A mudança vale na próxima vez que entrar.`
    : `${u.nome} agora tem o perfil ${perfil}.`),
  desativar: (u) => `O acesso de ${u.nome} foi desativado. Continua no fim desta lista, com o botão Reativar.`,
  // Desativar derruba o convite aberto: quem nunca entrou precisa de um convite novo para voltar.
  reativar: (u) => (u.convite?.estado === 'pendente'
    ? `O acesso de ${u.nome} voltou. O convite antigo deixou de valer: use Reenviar convite.`
    : `${u.nome} pode entrar de novo.`),
  marcarDona: (u) => `${u.nome} agora é dona.`,
  desmarcarDona: (u) => `${u.nome} não é mais dona.`,
}
const FALHOU = {
  convite: 'O convite não saiu',
  reenviar: 'O convite não foi reenviado',
  perfil: 'O perfil não mudou',
  desativar: 'O acesso continua ativo',
  reativar: 'O acesso continua desativado',
  marcarDona: 'A marca de dona não foi posta',
  desmarcarDona: 'A marca de dona continua',
}
export const fraseDeRetorno = (acao, usuario, perfil) => FEITO[acao](usuario, perfil)
// 429 no reenvio é o limite por pessoa da API (ConvitesDeAcesso.EmissoesPorHora).
export const fraseDeFalha = (acao, falha) => (acao === 'reenviar' && falha?.status === 429
  ? 'Limite de 3 envios por hora para a mesma pessoa. Tente de novo mais tarde.'
  : `${FALHOU[acao]}: ${mensagemDaFalha(falha)}`)

// Filtro do histórico: duas pessoas com o mesmo nome se distinguem pelo e-mail.
export function opcoesDePessoas(pessoas) {
  const vezes = new Map()
  for (const p of pessoas) vezes.set(chave(p.nome), (vezes.get(chave(p.nome)) ?? 0) + 1)
  return [...pessoas]
    .sort((a, b) => a.nome.localeCompare(b.nome, 'pt-BR') || String(a.email).localeCompare(String(b.email), 'pt-BR'))
    .map((p) => ({ valor: p.usuarioId, rotulo: vezes.get(chave(p.nome)) > 1 ? `${p.nome} (${p.email})` : p.nome }))
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

// O que a tela do convite diz quando não há mais o que preencher. Um botão só, "Entrar", em todas.
export const SAIDAS_DO_CONVITE = {
  pronto: { titulo: 'Senha criada', texto: 'Agora é só entrar com o seu e-mail e a senha que você escolheu.' },
  vencido: {
    titulo: 'Este convite já não vale',
    texto: 'Se você já criou a sua senha, é só entrar. Se ainda não criou, peça à dona para reenviar o convite.',
  },
  // A página foi aberta sem o link (recarregada em outra aba, por exemplo): o convite pode estar valendo.
  semToken: {
    titulo: 'Abra o link do convite de novo',
    texto: 'Use o botão do e-mail que você recebeu. Se já criou a sua senha, é só entrar.',
  },
  limitado: {
    titulo: 'Muitas tentativas seguidas',
    texto: 'Espere uns minutos e abra o link do e-mail de novo. Se já criou a sua senha, é só entrar.',
  },
}

// O token do convite chega no fragmento (`#/convite?t=...`), fora do log de acesso do servidor.
export function tokenDoConvite(hash) {
  const achado = /^#\/convite\?(.*)$/.exec(hash ?? '')
  if (!achado) return null
  const token = new URLSearchParams(achado[1]).get('t')
  return token && token.trim() ? token.trim() : null
}
export const ehRotaDeConvite = (hash) => /^#\/convite(\?|$)/.test(hash ?? '')

// #2070: o convite também se aceita entrando com a conta Google do e-mail convidado (a API só aceita com o
// convite aberto). Mesmo formato do `google` da TelaLogin, mas nunca entra sozinho: o aparelho da casa pode ter
// a conta Google de outra pessoa já escolhida. Entrou: `aoEntrar` conclui o convite.
export function googleDoConvite(google, aoEntrar) {
  if (!google) return null
  return {
    buscarClientId: google.buscarClientId,
    opcoes: () => ({ auto_select: false, oferecer: false }),
    entrar: async (idToken) => {
      await google.entrar(idToken)
      aoEntrar()
    },
  }
}
// 401 sem frase da API é a conta Google sem convite nem acesso; com frase (convite vencido), vale a da API.
export const fraseDaFalhaDoGoogleNoConvite = (falha) => (falha?.status === 401 && falha?.codigo === 'HTTP_401'
  ? 'Esta conta Google não tem convite. Use a conta Google do e-mail que recebeu o convite, ou crie uma senha.'
  : falha?.message ?? 'Não deu para entrar com o Google. Tente de novo.')

// Auditoria: a ação gravada pela API vira o que aconteceu, em palavras da casa.
const ACOES = {
  login: 'Entrou',
  login_novo_dispositivo: 'Entrou em outro aparelho',
  logout: 'Saiu',
  'usuario-convidado': 'Convite enviado',
  'usuario-criado': 'Acesso criado',
  'aceitar-convite': 'Aceitou o convite e criou a senha',
  'convite-reenviado': 'Convite reenviado',
  'perfil-trocado': 'Perfil trocado',
  'usuario-desativado': 'Acesso desativado',
  'usuario-reativado': 'Acesso reativado',
  login_google: 'Entrou com a conta Google',
  'forgot-password': 'Pediu para criar uma senha nova',
  'forgot-password-limitado': 'Pediu senha nova muitas vezes seguidas',
  'forgot-password-convite': 'Pediu senha nova e recebeu o convite de novo',
  'reset-password': 'Criou uma senha nova pelo link do e-mail',
  'reset-password-code': 'Criou uma senha nova com o código',
  'admin-troca-email-solicitada': 'Troca de e-mail pedida',
  'admin-troca-email-convidado': 'E-mail trocado e convite enviado de novo',
  'email-alterado': 'E-mail trocado',
  'email-confirmado': 'E-mail confirmado',
  'telefone-verificado': 'Telefone confirmado',
  'loja.fechada_no_horario': 'Fechou a loja no horário de atendimento',
}
// Só as frases novas trazem o nome de quem fez ("... por Baba"); o resto do detalhe é texto de máquina.
// Estas ações têm " por " no detalhe sem ser frase: "aceito por e-mail", "solicitada por <identificador>".
const DETALHE_DE_MAQUINA = new Set(['aceitar-convite', 'admin-troca-email-solicitada', 'admin-troca-email-convidado'])
// Linhas antigas guardaram o nível e o canal em jargão: "(Admin); WhatsApp: nao".
const limpar = (texto) => texto
  .replace(/\s*\((Admin|SuperAdmin|Gerente|Operador)\)/g, '')
  .replace(/;\s*WhatsApp:\s*sim/i, ', também pelo WhatsApp')
  .replace(/;\s*WhatsApp:\s*n[aã]o/i, '')
  .trim()

export function linhaDeAuditoria(item) {
  const conhecido = ACOES[item.acao]
  const detalhe = item.detalhes?.includes(' por ') && !DETALHE_DE_MAQUINA.has(item.acao) ? limpar(item.detalhes) : null
  return {
    id: item.id,
    quando: item.dataHora,
    pessoa: item.usuarioNome,
    usuarioId: item.usuarioId,
    acao: item.acao,
    // O detalhe com autor já é a frase inteira ("Perfil trocado de Cozinha para Atendimento por Baba"); sem ele, vale a ação.
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

// "Carregar mais antigos" pode trazer de novo uma linha que já está na tela (entrou linha nova entre uma busca e outra).
export function juntarPaginas(anteriores, novas) {
  const vistos = new Set(anteriores.map((l) => l.id))
  return [...anteriores, ...novas.filter((l) => !vistos.has(l.id))]
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
