/* eslint-disable no-console */
// Prova da #1753: as regras da tela de Usuários, do convite e do histórico de acesso.
// A homologação de 13/10 achou a dona se desativando e ficando de fora, e uma auditoria que só
// falava em identificadores. Aqui fica o que a tela promete, sem navegador.
//
// #2070: os botões de cada linha vêm das `acoes` que a API calcula pela política de hierarquia,
// nunca de regra local; a linha da dona ganha o selo; o convite só oferece "como dona" com perfil
// de nível Admin atribuível e pede o e-mail duas vezes; o suporte escreve o motivo ao tirar a marca.
// A aba é montada de verdade no react-dom (DOM mínimo de `tela-no-node.mjs`), com a API simulada no
// envelope real: o que se confere é o que sai para a API depois de cada toque.
//
//   node ferramentas/prova-1753-usuarios.mjs

import assert from 'node:assert/strict'
import {
  botoesDe, campoDe, clicar, comTela, digitar, documento, elementos, enviar, escolher, esperar, guardado, marcar, resposta,
  temCampo, textoDe, caixaDe,
} from './tela-no-node.mjs'

// --- A API simulada: anota cada chamada e responde no envelope da EasyStock.Api.
const SESSAO = { token: 'jwt-de-teste', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa-1' }, usuario: { id: 'eu', nivel: 'Admin' } }
const SUPORTE = { ...SESSAO, usuario: { id: 'suporte', nivel: 'SuperAdmin' } }
guardado.set('easystok.sessao', JSON.stringify(SESSAO))
const api = { usuarios: [], perfis: [], chamadas: [] }
globalThis.fetch = async (url, opcoes = {}) => {
  const metodo = opcoes.method ?? 'GET'
  api.chamadas.push({ metodo, url, corpo: opcoes.body === undefined ? undefined : JSON.parse(opcoes.body) })
  if (metodo === 'GET' && url.startsWith('/api/usuarios/perfis?')) return resposta(200, { data: api.perfis })
  if (metodo === 'GET' && url.startsWith('/api/usuarios?')) return resposta(200, { data: api.usuarios })
  if (metodo === 'POST' && url === '/api/usuarios') return resposta(201, { data: { id: 'nova' } })
  return resposta(204)
}
const escritas = () => api.chamadas.filter((c) => c.metodo !== 'GET').map(({ metodo, url, corpo }) => ({ metodo, url, corpo }))

const {
  ACOES_DA_LINHA, AVISO_DE_VERSAO_ANTIGA, CONTROLES_DA_LINHA, acoesDoUsuario, agruparPorDia, condensarAuditoria, confirmacaoDaMarcaDeDona,
  controlesDaPessoa, ehRotaDeConvite, ehSuporteDaSessao, erroDoConvite, erroDoMotivo, estadoDoConvite, falhaDoConvite, fraseDeFalha,
  fraseDeRetorno, instanteDaApi, juntarPaginas, linhaDeAuditoria, listaDeVersaoAntiga, mensagemDaFalha, MENSAGEM_DO_CODIGO, opcoesDePessoas,
  opcoesDoPerfilDaPessoa, ordenarPerfis, perfisAtribuiveis, podeConvidarComoDona, regrasDaSenha, resumoDoPerfil, senhaValida,
  SAIDAS_DO_CONVITE, situacaoDoUsuario, tokenDoConvite, DICA_DO_EMAIL_DO_CONVITE,
} = await import('../src/dominio/usuarios.js')

let passaram = 0
const falharam = []
const conferir = (nome, teste) => {
  try { teste(); passaram += 1; console.log('ok  ' + nome) } catch (erro) { falharam.push(nome); console.log('FALHOU  ' + nome + '\n   ' + erro.message.split('\n').join('\n   ')) }
}
const conferirAssincrono = async (nome, teste) => {
  try { await teste(); passaram += 1; console.log('ok  ' + nome) } catch (erro) { falharam.push(nome); console.log('FALHOU  ' + nome + '\n   ' + erro.message.split('\n').join('\n   ')) }
}

// --- Dados de teste: os perfis da casa como a API devolve (nível e se o ator pode dar).
const ADMINISTRACAO = { perfilId: 'p-adm', nome: 'Administração', nivel: 'Admin', atribuivel: true }
const GERENCIA = { perfilId: 'p-ger', nome: 'Gerência', nivel: 'Gerente', atribuivel: true }
const ATENDIMENTO = { perfilId: 'p-ate', nome: 'Atendimento', nivel: 'Operador', atribuivel: true }
const COZINHA = { perfilId: 'p-coz', nome: 'Cozinha', nivel: 'Operador', atribuivel: true }
const PERFIS = [ADMINISTRACAO, GERENCIA, ATENDIMENTO, COZINHA]
const nenhuma = Object.fromEntries((ACOES_DA_LINHA ?? []).map((a) => [a, false]))
const pessoa = (extra = {}) => ({
  usuarioId: 'ana', nome: 'Ana Souza', email: 'ana@exemplo.test', ativo: true, convite: { estado: 'aceito' },
  perfilId: 'p-ate', perfil: 'Atendimento', dona: false, acoes: { ...nenhuma }, ultimoAcessoEm: null, ...extra,
})

// ---------------------------------------------------------------- botões pelas `acoes` da API

conferir('as nove ações da política viram as oito chaves da linha, nesta ordem', () => {
  assert.deepEqual(ACOES_DA_LINHA, ['editarNome', 'trocarEmail', 'trocarPerfil', 'desativar', 'reativar', 'reenviarConvite', 'marcarDona', 'desmarcarDona'])
  assert.deepEqual(CONTROLES_DA_LINHA, ['trocarPerfil', 'reenviarConvite', 'marcarDona', 'desmarcarDona', 'desativar', 'reativar'])
})

conferir('sem `acoes` (resposta antiga ou incompleta) a linha não oferece nada; só `true` liga', () => {
  assert.deepEqual(acoesDoUsuario({ usuarioId: 'ana', ativo: true }), nenhuma)
  assert.deepEqual(acoesDoUsuario({ acoes: { trocarPerfil: 'sim', desativar: 1, reativar: true } }), { ...nenhuma, reativar: true })
  assert.deepEqual(controlesDaPessoa({ acoes: null }), [])
})

conferir('a regra local não decide: a própria pessoa, desativada ou com convite aberto segue só as `acoes`', () => {
  // Antes a tela escondia os botões de quem era "eu" e de quem estava desativado; agora quem diz é a política.
  const tudo = Object.fromEntries(ACOES_DA_LINHA.map((a) => [a, true]))
  for (const estado of [{ usuarioId: 'eu' }, { ativo: false }, { convite: { estado: 'pendente' } }, { ativo: false, convite: { estado: 'nenhum' } }]) {
    assert.deepEqual(controlesDaPessoa(pessoa({ ...estado, acoes: tudo })), CONTROLES_DA_LINHA)
    assert.deepEqual(controlesDaPessoa(pessoa({ ...estado, acoes: nenhuma })), [])
  }
})

conferir('resposta da API de antes da hierarquia (linha sem `acoes` ou perfil sem `atribuivel`) é reconhecida', () => {
  // O console pode subir antes da API nova (ou a API voltar atrás): sem isto a aba ficava sem botões e sem dizer por quê.
  assert.equal(listaDeVersaoAntiga({ usuarios: [pessoa()], perfis: PERFIS }), false)
  assert.equal(listaDeVersaoAntiga({ usuarios: [pessoa({ acoes: { ...nenhuma } })], perfis: PERFIS }), false, 'tudo falso ainda é a API nova')
  assert.equal(listaDeVersaoAntiga({ usuarios: [{ usuarioId: 'ana', ativo: true }], perfis: PERFIS }), true)
  assert.equal(listaDeVersaoAntiga({ usuarios: [pessoa({ acoes: null })], perfis: PERFIS }), true)
  assert.equal(listaDeVersaoAntiga({ usuarios: [pessoa()], perfis: [{ perfilId: 'p-adm', nome: 'Administração', nivel: 'Admin' }] }), true)
  assert.equal(listaDeVersaoAntiga({ usuarios: [], perfis: [] }), false)
  assert.equal(AVISO_DE_VERSAO_ANTIGA, 'A lista veio de uma versão antiga do sistema. As ações e o convite voltam quando a atualização terminar.')
})

conferir('em cada uma das 256 combinações de `acoes`, os controles são exatamente os ligados', () => {
  for (let i = 0; i < 2 ** ACOES_DA_LINHA.length; i += 1) {
    const acoes = Object.fromEntries(ACOES_DA_LINHA.map((a, j) => [a, Boolean(i & (1 << j))]))
    assert.deepEqual(controlesDaPessoa(pessoa({ acoes })), CONTROLES_DA_LINHA.filter((c) => acoes[c]), `combinação ${i}`)
  }
})

// ---------------------------------------------------------------- perfis da casa

conferir('os perfis da casa vêm antes do legado, na ordem Administração, Gerência, Atendimento, Cozinha', () => {
  const nomes = ordenarPerfis(['Operador', 'Cozinha', 'Admin', 'Gerência', 'Atendimento', 'Administração'].map((nome) => ({ nome }))).map((p) => p.nome)
  assert.deepEqual(nomes, ['Administração', 'Gerência', 'Atendimento', 'Cozinha', 'Admin', 'Operador'])
  // O nome sem acento (cadastro antigo ou digitado à mão) cai no mesmo lugar.
  assert.deepEqual(ordenarPerfis([{ nome: 'Cozinha' }, { nome: 'Gerencia' }, { nome: 'ADMINISTRACAO' }]).map((p) => p.nome), ['ADMINISTRACAO', 'Gerencia', 'Cozinha'])
})

conferir('cada perfil da casa tem um resumo curto e fiel à hierarquia; "Dona" não é perfil e não tem resumo', () => {
  // Administração não mexe na dona nem em outra Administração: o resumo diz de quem cuida, sem "para baixo".
  assert.equal(resumoDoPerfil('Administração'), 'Tudo; na equipe, cuida de Gerência, Atendimento e Cozinha')
  assert.equal(resumoDoPerfil('Gerência'), 'Toda a operação, com estoque e relatórios, menos Configurações')
  assert.equal(resumoDoPerfil('Atendimento'), 'Conversas, pedidos, cardápio, caixa e entregas')
  assert.equal(resumoDoPerfil('Cozinha'), 'Fila de preparo e produção')
  assert.equal(resumoDoPerfil('Dona'), null)
  assert.equal(resumoDoPerfil(null), null)
  for (const p of PERFIS) assert.ok(resumoDoPerfil(p.nome).length <= 64, `${p.nome}: resumo curto`)
  for (const p of PERFIS) assert.doesNotMatch(resumoDoPerfil(p.nome), /para baixo|abaixo|acima/, `${p.nome}: sem jargão de hierarquia`)
})

conferir('só os perfis com `atribuivel` entram nas opções; o perfil atual da pessoa sempre aparece', () => {
  const comAdminTravado = [{ ...ADMINISTRACAO, atribuivel: false }, GERENCIA, ATENDIMENTO, { ...COZINHA, atribuivel: undefined }]
  assert.deepEqual(perfisAtribuiveis(comAdminTravado).map((p) => p.perfilId), ['p-ger', 'p-ate'])
  assert.deepEqual(opcoesDoPerfilDaPessoa(pessoa({ perfilId: 'p-ate', perfil: 'Atendimento' }), comAdminTravado).map((o) => o.valor), ['p-ger', 'p-ate'])
  assert.deepEqual(opcoesDoPerfilDaPessoa(pessoa({ perfilId: 'p-adm', perfil: 'Administração' }), comAdminTravado),
    [{ valor: 'p-adm', rotulo: 'Administração' }, { valor: 'p-ger', rotulo: 'Gerência' }, { valor: 'p-ate', rotulo: 'Atendimento' }])
  assert.deepEqual(opcoesDoPerfilDaPessoa(pessoa({ perfilId: null, perfil: null }), comAdminTravado).map((o) => o.rotulo), ['Sem perfil', 'Gerência', 'Atendimento'])
})

// ---------------------------------------------------------------- convite

conferir('"Convidar como dona" só aparece com perfil de nível Admin que o ator pode dar', () => {
  assert.equal(podeConvidarComoDona(ADMINISTRACAO), true)
  assert.equal(podeConvidarComoDona({ ...ADMINISTRACAO, atribuivel: false }), false)
  assert.equal(podeConvidarComoDona(GERENCIA), false)
  assert.equal(podeConvidarComoDona(ATENDIMENTO), false)
  assert.equal(podeConvidarComoDona({ perfilId: 'x', nome: 'Suporte', nivel: 'SuperAdmin', atribuivel: true }), false)
  assert.equal(podeConvidarComoDona(null), false)

  const vazio = estadoDoConvite({ nome: '', email: '', emailDeNovo: '', perfilId: '', dona: false }, PERFIS)
  assert.equal(vazio.ofereceDona, false)
  assert.equal(estadoDoConvite({ perfilId: 'p-adm', dona: false }, PERFIS).ofereceDona, true)
  assert.equal(estadoDoConvite({ perfilId: 'p-ger', dona: true }, PERFIS).ofereceDona, false)
  // Marcou "como dona" e depois trocou para Gerência: a marca não vai junto.
  assert.equal(estadoDoConvite({ perfilId: 'p-ger', dona: true }, PERFIS).comoDona, false)
  // Administração sem `atribuivel` (quem convida é Administração, não dona): nem aparece na lista.
  const travado = estadoDoConvite({ perfilId: 'p-adm', dona: true }, [{ ...ADMINISTRACAO, atribuivel: false }, GERENCIA])
  assert.deepEqual(travado.opcoes.map((o) => o.valor), ['', 'p-ger'])
  assert.equal(travado.ofereceDona, false)
  assert.equal(travado.comoDona, false)
})

conferir('convidar como dona pede o e-mail duas vezes e confere, sem diferença de maiúscula ou espaço', () => {
  const comoDona = estadoDoConvite({ perfilId: 'p-adm', dona: true }, PERFIS)
  assert.equal(comoDona.comoDona, true)
  assert.equal(comoDona.pedeEmailDeNovo, true)
  assert.equal(estadoDoConvite({ perfilId: 'p-adm', dona: false }, PERFIS).pedeEmailDeNovo, false)
  const base = { nome: 'Bia', email: 'bia@exemplo.test', perfilId: 'p-adm', dona: true }
  assert.equal(erroDoConvite({ ...base, emailDeNovo: '' }), 'Digite o e-mail mais uma vez para conferir.')
  assert.equal(erroDoConvite({ ...base, emailDeNovo: 'bia@exemplo.tst' }), 'Os dois e-mails não são iguais. Confira e digite o mesmo nas duas vezes.')
  assert.equal(erroDoConvite({ ...base, emailDeNovo: ' BIA@exemplo.test ' }), null)
  // Sem a marca de dona, a segunda vez não é pedida.
  assert.equal(erroDoConvite({ ...base, dona: false, emailDeNovo: '' }), null)
})

conferir('cada erro do convite diz em qual campo está, para o campo ser marcado e receber o foco', () => {
  const base = { nome: 'Bia', email: 'bia@exemplo.test', perfilId: 'p-adm', dona: true, emailDeNovo: 'bia@exemplo.test' }
  assert.deepEqual(falhaDoConvite({ ...base, nome: ' ' }), { campo: 'nome', texto: 'Escreva o nome da pessoa.' })
  assert.deepEqual(falhaDoConvite({ ...base, email: 'bia@casa' }), { campo: 'email', texto: 'Confira o e-mail: o convite vai para ele.' })
  assert.deepEqual(falhaDoConvite({ ...base, perfilId: '' }), { campo: 'perfilId', texto: 'Escolha o perfil.' })
  assert.deepEqual(falhaDoConvite({ ...base, emailDeNovo: '' }), { campo: 'emailDeNovo', texto: 'Digite o e-mail mais uma vez para conferir.' })
  assert.deepEqual(falhaDoConvite({ ...base, emailDeNovo: 'bia@exemplo.tst' }),
    { campo: 'emailDeNovo', texto: 'Os dois e-mails não são iguais. Confira e digite o mesmo nas duas vezes.' })
  assert.equal(falhaDoConvite(base), null)
})

conferir('convite sem nome, com e-mail torto ou sem perfil não sai', () => {
  assert.equal(erroDoConvite({ nome: ' ', email: 'a@b.co', perfilId: 'p' }), 'Escreva o nome da pessoa.')
  assert.equal(erroDoConvite({ nome: 'Ana', email: 'ana@casa', perfilId: 'p' }), 'Confira o e-mail: o convite vai para ele.')
  assert.equal(erroDoConvite({ nome: 'Ana', email: 'ana@casa.com', perfilId: '' }), 'Escolha o perfil.')
  assert.equal(erroDoConvite({ nome: 'Ana', email: ' ana@casa.com ', perfilId: 'p' }), null)
})

conferir('o convite explica que a pessoa entra com a conta Google do e-mail', () => {
  assert.match(DICA_DO_EMAIL_DO_CONVITE, /a pessoa entra com a conta Google deste e-mail/)
})

// ---------------------------------------------------------------- marca de dona e motivo do suporte

conferir('só a sessão de nível SuperAdmin é o suporte', () => {
  assert.equal(ehSuporteDaSessao({ usuario: { nivel: 'SuperAdmin' } }), true)
  for (const nivel of ['Admin', 'Gerente', 'Operador', 'Visualizador', undefined]) assert.equal(ehSuporteDaSessao({ usuario: { nivel } }), false)
  assert.equal(ehSuporteDaSessao(null), false)
})

conferir('marcar e tirar a marca de dona confirmam antes; o suporte escreve o motivo só ao tirar a marca', () => {
  const ana = pessoa({ nome: 'Ana Souza', perfil: 'Administração' })
  const marcarAna = confirmacaoDaMarcaDeDona('marcarDona', ana, false)
  assert.equal(marcarAna.confirmar, 'Marcar Ana como dona')
  assert.equal(marcarAna.pedeMotivo, false)
  assert.match(marcarAna.texto, /^Como dona, Ana pode tudo/)
  const tirar = confirmacaoDaMarcaDeDona('desmarcarDona', ana, false)
  assert.equal(tirar.confirmar, 'Tirar a marca de dona de Ana')
  assert.equal(tirar.texto, 'Ana continua com o perfil Administração, mas deixa de ser dona.')
  // A especificação (tabela do suporte) e a política da API só exigem motivo do suporte ao tirar a marca de uma dona.
  assert.equal(confirmacaoDaMarcaDeDona('marcarDona', ana, true).pedeMotivo, false)
  assert.equal(confirmacaoDaMarcaDeDona('desmarcarDona', ana, true).pedeMotivo, true)
})

conferir('o motivo do suporte tem 10 caracteres ou mais, sem contar os espaços das pontas', () => {
  assert.equal(erroDoMotivo(''), 'Escreva o motivo com pelo menos 10 caracteres. Ele fica no histórico.')
  assert.equal(erroDoMotivo('   curto    '), 'Escreva o motivo com pelo menos 10 caracteres. Ele fica no histórico.')
  assert.equal(erroDoMotivo(' 123456789 '), 'Escreva o motivo com pelo menos 10 caracteres. Ele fica no histórico.')
  assert.equal(erroDoMotivo(' 1234567890 '), null)
  assert.equal(erroDoMotivo(null), 'Escreva o motivo com pelo menos 10 caracteres. Ele fica no histórico.')
})

// ---------------------------------------------------------------- mensagens dos códigos da API

conferir('cada código de recusa da hierarquia vira uma frase em português', () => {
  assert.deepEqual(Object.keys(MENSAGEM_DO_CODIGO).sort(), [
    'DONA_CONTA_NAO_PROVADA', 'DONA_EMAIL_EXISTENTE', 'DONA_NAO_ADMIN', 'DONA_PROTEGIDA', 'DONA_SUPORTE', 'HIERARQUIA', 'MOTIVO_OBRIGATORIO',
    'ULTIMA_DONA', 'USUARIO_PROPRIO',
  ])
  // Quem recebe HIERARQUIA pode ser a Administração mexendo na dona ou em outra Administração: a frase não manda pedir à dona.
  assert.equal(mensagemDaFalha({ status: 403, codigo: 'HIERARQUIA', message: 'Forbidden' }), 'Seu perfil não permite fazer isso com esta pessoa.')
  // 400 da API quando o suporte não manda o motivo (HierarquiaDeUsuarios): a tela não deixa sair sem ele, mas a frase existe.
  assert.equal(mensagemDaFalha({ status: 400, codigo: 'MOTIVO_OBRIGATORIO', message: 'x' }),
    'O suporte precisa escrever o motivo, com pelo menos 10 caracteres. Ele fica no histórico.')
  assert.equal(mensagemDaFalha({ status: 409, codigo: 'DONA_EMAIL_EXISTENTE', message: 'x' }),
    'Este e-mail já tem conta no sistema. Convide a dona com um e-mail que ainda não tenha conta.')
  assert.equal(mensagemDaFalha({ status: 409, codigo: 'ULTIMA_DONA', message: 'x' }), 'A casa precisa de pelo menos uma dona. Marque outra pessoa como dona antes.')
  assert.equal(mensagemDaFalha({ status: 409, codigo: 'DONA_NAO_ADMIN', message: 'x' }), 'Só quem tem o perfil Administração pode ser dona. Troque o perfil antes.')
  assert.equal(mensagemDaFalha({ status: 409, codigo: 'DONA_CONTA_NAO_PROVADA', message: 'x' }),
    'Esta pessoa ainda não confirmou a conta. Ela precisa aceitar o convite pelo e-mail ou entrar com a conta Google antes.')
  assert.equal(mensagemDaFalha({ status: 400, codigo: 'DONA_SUPORTE', message: 'x' }), 'O acesso do suporte não pode ser marcado como dona.')
  assert.equal(mensagemDaFalha({ status: 403, codigo: 'USUARIO_PROPRIO', message: 'x' }), 'Você não pode mudar o seu próprio acesso.')
  assert.equal(mensagemDaFalha({ status: 409, codigo: 'DONA_PROTEGIDA', message: 'x' }),
    'Ninguém da casa troca o perfil nem desativa uma dona. Antes é preciso tirar a marca de dona.')
  assert.equal(mensagemDaFalha({ status: 500, codigo: 'ERRO_NOVO', message: 'Texto da API' }), 'Texto da API')
  for (const texto of Object.values(MENSAGEM_DO_CODIGO)) assert.doesNotMatch(texto, /[A-Z]{3,}_|Forbidden|Conflict/, 'sem código nem inglês')
  // A conta que já existe nunca vira sugestão de marcar como dona (A4).
  assert.doesNotMatch(MENSAGEM_DO_CODIGO.DONA_EMAIL_EXISTENTE, /marc/i)
})

conferir('a falha de cada ação diz o que não mudou e por quê, com a frase do código', () => {
  const hierarquia = { status: 403, codigo: 'HIERARQUIA', message: 'Forbidden' }
  assert.equal(fraseDeFalha('perfil', hierarquia), 'O perfil não mudou: Seu perfil não permite fazer isso com esta pessoa.')
  assert.equal(fraseDeFalha('convite', hierarquia), 'O convite não saiu: Seu perfil não permite fazer isso com esta pessoa.')
  assert.equal(fraseDeFalha('convite', { status: 409, codigo: 'DONA_EMAIL_EXISTENTE' }),
    'O convite não saiu: Este e-mail já tem conta no sistema. Convide a dona com um e-mail que ainda não tenha conta.')
  assert.equal(fraseDeFalha('marcarDona', { status: 409, codigo: 'DONA_NAO_ADMIN' }),
    'Não deu para marcar como dona: Só quem tem o perfil Administração pode ser dona. Troque o perfil antes.')
  assert.equal(fraseDeFalha('desmarcarDona', { status: 409, codigo: 'ULTIMA_DONA' }),
    'A marca de dona continua: A casa precisa de pelo menos uma dona. Marque outra pessoa como dona antes.')
  assert.equal(fraseDeFalha('desativar', hierarquia), 'O acesso continua ativo: Seu perfil não permite fazer isso com esta pessoa.')
})

conferir('a frase de retorno diz o que mudou, e só fala em entrar de novo para quem já entrou', () => {
  const ana = { nome: 'Ana', email: 'ana@casa.com', ultimoAcessoEm: '2026-10-13T20:00:00' }
  const nova = { nome: 'Bia', email: 'bia@casa.com', ultimoAcessoEm: null }
  assert.equal(fraseDeRetorno('perfil', ana, 'Cozinha'), 'Ana agora tem o perfil Cozinha. A mudança vale na próxima vez que entrar.')
  assert.equal(fraseDeRetorno('perfil', nova, 'Cozinha'), 'Bia agora tem o perfil Cozinha.')
  assert.equal(fraseDeRetorno('desativar', ana), 'O acesso de Ana foi desativado. Continua no fim desta lista, com o botão Reativar.')
  assert.equal(fraseDeRetorno('reativar', ana), 'Ana pode entrar de novo.')
  assert.match(fraseDeRetorno('convite', nova), /bia@casa\.com/)
  assert.equal(fraseDeRetorno('convite', { ...nova, dona: true }), 'Convite de dona enviado para bia@casa.com. Chega em até um minuto.')
  assert.equal(fraseDeRetorno('marcarDona', ana), 'Ana agora é dona.')
  assert.equal(fraseDeRetorno('desmarcarDona', ana), 'Ana não é mais dona.')
})

conferir('reenvio no limite da hora explica o limite; as outras falhas dizem o que não mudou', () => {
  assert.equal(fraseDeFalha('reenviar', { status: 429, message: 'Too many' }),
    'Limite de 3 envios por hora para a mesma pessoa. Tente de novo mais tarde.')
  assert.equal(fraseDeFalha('reenviar', { status: 500, message: 'caiu' }), 'O convite não foi reenviado: caiu')
  assert.equal(fraseDeFalha('perfil', { status: 429, message: 'calma' }), 'O perfil não mudou: calma')
})

// ---------------------------------------------------------------- a tela montada

const { createElement: h } = await import('react')
const { renderToStaticMarkup } = await import('react-dom/server')
const { AbaUsuarios, Pessoa, Convite } = await import('../src/features/gestao/usuarios/AbaUsuarios.jsx')
const { ContextoEstado } = await import('../src/aplicacao/contextos.js')
const AGORA = Date.parse('2026-10-26T12:00:00Z')
const textoDoHtml = (html) => html.replace(/<[^>]+>/g, '').trim()
const botoesDoHtml = (html) => [...html.matchAll(/<button\b[^>]*>(.*?)<\/button>/g)].map((m) => textoDoHtml(m[1]))
const linha = (usuario) => renderToStaticMarkup(h(Pessoa, {
  usuario, perfis: PERFIS, meuId: 'eu', agora: AGORA, ocupado: false, suporte: false, aoAgir() {}, aoAbrirConfirmacao() {},
}))
// Oráculo próprio da prova: os rótulos que a dona lê em cada controle.
const ROTULO = {
  reenviarConvite: 'Reenviar convite', marcarDona: 'Marcar como dona', desmarcarDona: 'Tirar a marca de dona', desativar: 'Desativar', reativar: 'Reativar',
}
// A aba inteira, com a sessão de quem está na tela e a lista que a API devolve.
const aba = (sessao) => h(ContextoEstado.Provider, { value: { sessao, agora: AGORA } }, h(AbaUsuarios))
const naAba = async (sessao, { usuarios, perfis = PERFIS }, fn) => {
  api.usuarios = usuarios
  api.perfis = perfis
  api.chamadas.length = 0
  await comTela(aba(sessao), async (tela) => { await esperar(); await fn(tela) })
}
const alertasDe = (tela) => elementos(tela, (no) => no.getAttribute('role') === 'alert').map((no) => no.textContent)

conferir('linha montada: em cada combinação de `acoes`, os botões e o seletor de perfil batem com a API', () => {
  for (let i = 0; i < 2 ** ACOES_DA_LINHA.length; i += 1) {
    const acoes = Object.fromEntries(ACOES_DA_LINHA.map((a, j) => [a, Boolean(i & (1 << j))]))
    // Estado local variado de propósito: ele não pode ligar nem desligar botão nenhum.
    const html = linha(pessoa({ acoes, usuarioId: i % 2 ? 'eu' : 'ana', ativo: i % 3 !== 0, convite: { estado: i % 5 ? 'aceito' : 'pendente' } }))
    const esperados = CONTROLES_DA_LINHA.filter((c) => acoes[c] && c !== 'trocarPerfil').map((c) => ROTULO[c])
    assert.deepEqual(botoesDoHtml(html), esperados, `combinação ${i}`)
    assert.equal(html.includes('<select'), acoes.trocarPerfil, `combinação ${i}: seletor de perfil`)
  }
})

conferir('linha montada: o selo "Dona" vem da marca da API, não do nome do perfil', () => {
  const selo = /<span class="pilula marca">Dona<\/span>/
  assert.match(linha(pessoa({ dona: true, perfil: 'Administração', perfilId: 'p-adm' })), selo)
  assert.doesNotMatch(linha(pessoa({ dona: false, perfil: 'Administração', perfilId: 'p-adm' })), selo)
  assert.doesNotMatch(linha(pessoa({ dona: false, perfil: 'Dona', perfilId: 'p-antigo' })), selo)
})

conferir('convite montado: só perfis atribuíveis, sem "como dona" antes de escolher o perfil, e o texto do Google', () => {
  const html = renderToStaticMarkup(h(Convite, { perfis: [{ ...ADMINISTRACAO, atribuivel: false }, GERENCIA, ATENDIMENTO, COZINHA], aoConvidar() {}, aoCancelar() {} }))
  assert.deepEqual([...html.matchAll(/<option value="([^"]*)"/g)].map((m) => m[1]), ['', 'p-ger', 'p-ate', 'p-coz'])
  assert.doesNotMatch(html, /Convidar como dona|Confirme o e-mail/)
  assert.ok(html.includes(DICA_DO_EMAIL_DO_CONVITE), 'a dica do e-mail fala da conta Google')
})

await conferirAssincrono('convite de dona na aba: a caixa só com Administração, o e-mail conferido duas vezes e `dona: true` no POST', async () => {
  await naAba(SESSAO, { usuarios: [] }, async (tela) => {
    clicar(tela, 'Convidar pessoa')
    digitar(tela, 'Nome', 'Bia Lima')
    digitar(tela, 'E-mail', 'bia@exemplo.test')
    escolher(tela, 'Perfil', 'p-ger')
    assert.equal(caixaDe(tela, 'Convidar como dona'), null, 'Gerência não oferece a marca')
    escolher(tela, 'Perfil', 'p-adm')
    assert.equal(temCampo(tela, 'Confirme o e-mail'), false, 'sem a marca, o e-mail não é pedido de novo')
    marcar(tela, 'Convidar como dona')
    assert.equal(temCampo(tela, 'Confirme o e-mail'), true)

    digitar(tela, 'Confirme o e-mail', 'bia@exemplo.tst')
    enviar(tela)
    await esperar()
    assert.deepEqual(alertasDe(tela), ['Os dois e-mails não são iguais. Confira e digite o mesmo nas duas vezes.'])
    assert.deepEqual(escritas(), [], 'e-mail diferente não sai para a API')

    digitar(tela, 'Confirme o e-mail', ' BIA@exemplo.test ')
    enviar(tela)
    await esperar()
    assert.deepEqual(escritas(), [{
      metodo: 'POST', url: '/api/usuarios',
      corpo: { empresaId: 'empresa-1', nome: 'Bia Lima', email: 'bia@exemplo.test', senha: null, perfilId: 'p-adm', lojaId: null, dona: true },
    }])
    assert.match(textoDe(tela), /Convite de dona enviado para bia@exemplo\.test\. Chega em até um minuto\./)
  })
})

await conferirAssincrono('convite na aba: marcou "como dona" e trocou para Gerência, a caixa some e o POST vai com `dona: false`', async () => {
  await naAba(SESSAO, { usuarios: [] }, async (tela) => {
    clicar(tela, 'Convidar pessoa')
    digitar(tela, 'Nome', 'Caio Reis')
    digitar(tela, 'E-mail', 'caio@exemplo.test')
    escolher(tela, 'Perfil', 'p-adm')
    marcar(tela, 'Convidar como dona')
    escolher(tela, 'Perfil', 'p-ger')
    assert.equal(caixaDe(tela, 'Convidar como dona'), null)
    assert.equal(temCampo(tela, 'Confirme o e-mail'), false)
    enviar(tela)
    await esperar()
    assert.deepEqual(escritas().map((e) => [e.metodo, e.url, e.corpo.perfilId, e.corpo.dona]), [['POST', '/api/usuarios', 'p-ger', false]])
  })
})

await conferirAssincrono('suporte marca a primeira dona: confirma sem motivo (a política não pede) e o PUT vai sem ele', async () => {
  const ana = pessoa({ perfil: 'Administração', perfilId: 'p-adm', acoes: { ...nenhuma, marcarDona: true } })
  await naAba(SUPORTE, { usuarios: [ana] }, async (tela) => {
    clicar(tela, 'Marcar como dona')
    await esperar()
    assert.equal(temCampo(tela, 'Motivo do suporte'), false, 'marcar não pede motivo, nem ao suporte')
    assert.equal(documento.activeElement?.textContent, 'Cancelar')
    clicar(tela, 'Marcar Ana como dona')
    await esperar()
    assert.deepEqual(escritas(), [{ metodo: 'PUT', url: '/api/usuarios/ana/dona?empresaId=empresa-1', corpo: { dona: true } }])
    assert.match(textoDe(tela), /Ana Souza agora é dona\./)
  })
})

await conferirAssincrono('suporte tira a marca de dona: o motivo é pedido, o erro fica ligado ao campo e o PUT leva o motivo', async () => {
  const ana = pessoa({ perfil: 'Administração', perfilId: 'p-adm', dona: true, acoes: { ...nenhuma, desmarcarDona: true } })
  await naAba(SUPORTE, { usuarios: [ana] }, async (tela) => {
    clicar(tela, 'Tirar a marca de dona')
    await esperar()
    const motivo = campoDe(tela, 'Motivo do suporte')
    assert.equal(documento.activeElement, motivo, 'o foco vai para o motivo')

    clicar(tela, 'Tirar a marca de dona de Ana')
    await esperar()
    const [erro] = elementos(tela, (no) => no.getAttribute('role') === 'alert')
    assert.equal(erro?.textContent, 'Escreva o motivo com pelo menos 10 caracteres. Ele fica no histórico.')
    assert.equal(motivo.getAttribute('aria-invalid'), 'true', 'o campo diz que está errado')
    assert.ok(erro.getAttribute('id'), 'o erro tem id')
    assert.ok((motivo.getAttribute('aria-describedby') ?? '').split(' ').includes(erro.getAttribute('id')), 'o campo aponta para o erro')
    assert.deepEqual(escritas(), [], 'sem motivo não sai nada')

    digitar(tela, 'Motivo do suporte', '  pedido da dona por telefone  ')
    assert.equal(campoDe(tela, 'Motivo do suporte').getAttribute('aria-invalid'), null, 'escrever tira o erro')
    clicar(tela, 'Tirar a marca de dona de Ana')
    await esperar()
    assert.deepEqual(escritas(), [{ metodo: 'PUT', url: '/api/usuarios/ana/dona?empresaId=empresa-1', corpo: { dona: false, motivo: 'pedido da dona por telefone' } }])
    assert.match(textoDe(tela), /Ana Souza não é mais dona\./)
    assert.equal(temCampo(tela, 'Motivo do suporte'), false, 'deu certo: a confirmação fecha')
  })
})

await conferirAssincrono('a dona tira a marca de outra: confirmação em grupo rotulado, foco em Cancelar, PUT com `dona: false` e sem motivo', async () => {
  const ana = pessoa({ perfil: 'Administração', perfilId: 'p-adm', dona: true, acoes: { ...nenhuma, desmarcarDona: true } })
  await naAba(SESSAO, { usuarios: [ana] }, async (tela) => {
    assert.deepEqual(botoesDe(tela).filter((b) => b !== 'Convidar pessoa'), ['Tirar a marca de dona'])
    clicar(tela, 'Tirar a marca de dona')
    await esperar()
    assert.equal(temCampo(tela, 'Motivo do suporte'), false, 'fora do suporte não há motivo')
    const [grupo] = elementos(tela, (no) => no.localName === 'fieldset')
    assert.ok(grupo, 'a confirmação é um grupo (fieldset)')
    const legendas = elementos(tela, (no) => no.localName === 'legend' && grupo.contains(no)).map((no) => no.textContent)
    assert.deepEqual(legendas, ['Tirar a marca de dona de Ana'], 'o nome do grupo é o que se confirma')
    const [descricao] = elementos(tela, (no) => no.getAttribute('id') === grupo.getAttribute('aria-describedby'))
    assert.equal(descricao?.textContent, 'Ana continua com o perfil Administração, mas deixa de ser dona.', 'e a descrição é o efeito')
    assert.equal(documento.activeElement?.textContent, 'Cancelar')
    assert.ok(grupo.contains(documento.activeElement), 'o foco está dentro do grupo: o leitor anuncia nome e descrição')

    clicar(tela, 'Tirar a marca de dona de Ana')
    await esperar()
    assert.deepEqual(escritas(), [{ metodo: 'PUT', url: '/api/usuarios/ana/dona?empresaId=empresa-1', corpo: { dona: false } }])
    assert.match(textoDe(tela), /Ana Souza não é mais dona\./)
  })
})

// O PUT da marca recusado pela API com o código dado; o resto segue a API simulada (e fica anotado).
const comMarcaRecusada = async (codigo, fn) => {
  const original = globalThis.fetch
  globalThis.fetch = async (url, opcoes = {}) => {
    if (opcoes.method !== 'PUT') return original(url, opcoes)
    api.chamadas.push({ metodo: 'PUT', url, corpo: JSON.parse(opcoes.body) })
    return resposta(409, { error: { code: codigo, message: 'Recusado pela política.' } })
  }
  try { await fn() } finally { globalThis.fetch = original }
}
const caminhos = () => api.chamadas.map((c) => `${c.metodo} ${c.url.split('?')[0]}`)
const selosDona = (tela) => elementos(tela, (no) => no.localName === 'span' && no.textContent === 'Dona')

await conferirAssincrono('recusa da API na marca: a frase do código aparece e nada fica marcado', async () => {
  const ana = pessoa({ perfil: 'Atendimento', acoes: { ...nenhuma, marcarDona: true } })
  await comMarcaRecusada('DONA_NAO_ADMIN', () => naAba(SESSAO, { usuarios: [ana] }, async (tela) => {
    clicar(tela, 'Marcar como dona')
    clicar(tela, 'Marcar Ana como dona')
    await esperar()
    assert.deepEqual(alertasDe(tela), ['Não deu para marcar como dona: Só quem tem o perfil Administração pode ser dona. Troque o perfil antes.'])
    const ordem = caminhos()
    assert.ok(ordem.lastIndexOf('GET /api/usuarios') > ordem.indexOf('PUT /api/usuarios/ana/dona'), 'a lista é lida de novo depois da recusa')
    assert.deepEqual(selosDona(tela), [], 'sem selo de dona: nada ligado antes da resposta')
    assert.deepEqual(botoesDe(tela).filter((b) => b !== 'Convidar pessoa'), ['Marcar como dona'], 'a linha volta ao que era')
  }))
})

await conferirAssincrono('recusa da API ao tirar a marca pelo suporte: a confirmação fica aberta com o motivo escrito', async () => {
  const ana = pessoa({ perfil: 'Administração', perfilId: 'p-adm', dona: true, acoes: { ...nenhuma, desmarcarDona: true } })
  await comMarcaRecusada('ULTIMA_DONA', () => naAba(SUPORTE, { usuarios: [ana] }, async (tela) => {
    clicar(tela, 'Tirar a marca de dona')
    await esperar()
    digitar(tela, 'Motivo do suporte', 'pedido da dona por telefone')
    clicar(tela, 'Tirar a marca de dona de Ana')
    await esperar()
    assert.deepEqual(alertasDe(tela), ['A marca de dona continua: A casa precisa de pelo menos uma dona. Marque outra pessoa como dona antes.'])
    assert.equal(campoDe(tela, 'Motivo do suporte').value, 'pedido da dona por telefone', 'o motivo não se perde')
    assert.equal(selosDona(tela).length, 1, 'continua dona')
    clicar(tela, 'Cancelar')
    await esperar()
    assert.equal(temCampo(tela, 'Motivo do suporte'), false)
    assert.deepEqual(botoesDe(tela).filter((b) => b !== 'Convidar pessoa'), ['Tirar a marca de dona'])
  }))
})

await conferirAssincrono('convite de dona acessível: a caixa lê a explicação e o e-mail diferente marca o campo e leva o foco a ele', async () => {
  await naAba(SESSAO, { usuarios: [] }, async (tela) => {
    clicar(tela, 'Convidar pessoa')
    digitar(tela, 'Nome', 'Bia Lima')
    digitar(tela, 'E-mail', 'bia@exemplo.test')
    escolher(tela, 'Perfil', 'p-adm')
    const caixa = caixaDe(tela, 'Convidar como dona')
    const [dica] = elementos(tela, (no) => no.localName === 'p' && no.textContent.startsWith('A dona pode tudo'))
    assert.ok(dica?.getAttribute('id'), 'a explicação tem id')
    assert.equal(caixa.getAttribute('aria-describedby'), dica.getAttribute('id'), 'a caixa aponta para a explicação')

    marcar(tela, 'Convidar como dona')
    digitar(tela, 'Confirme o e-mail', 'bia@exemplo.tst')
    enviar(tela)
    await esperar()
    const confirme = campoDe(tela, 'Confirme o e-mail')
    const [erro] = elementos(tela, (no) => no.getAttribute('role') === 'alert')
    assert.equal(erro?.textContent, 'Os dois e-mails não são iguais. Confira e digite o mesmo nas duas vezes.')
    assert.equal(confirme.getAttribute('aria-invalid'), 'true', 'o campo diz que está errado')
    assert.ok(erro.getAttribute('id'), 'o erro tem id')
    assert.ok((confirme.getAttribute('aria-describedby') ?? '').split(' ').includes(erro.getAttribute('id')), 'o campo aponta para o erro')
    assert.equal(documento.activeElement, confirme, 'o foco vai para o campo a corrigir')
    assert.equal(campoDe(tela, 'E-mail').getAttribute('aria-invalid'), null, 'só o campo errado fica marcado')

    digitar(tela, 'Confirme o e-mail', 'bia@exemplo.test')
    assert.equal(campoDe(tela, 'Confirme o e-mail').getAttribute('aria-invalid'), null, 'escrever tira o erro')
  })
})

await conferirAssincrono('API de antes da hierarquia: a aba diz por que não há ações, em vez de ficar sem botões calada', async () => {
  const semAcoes = { usuarioId: 'ana', nome: 'Ana Souza', email: 'ana@exemplo.test', ativo: true, convite: { estado: 'aceito' }, perfilId: 'p-ate', perfil: 'Atendimento', ultimoAcessoEm: null }
  const perfisAntigos = PERFIS.map(({ perfilId, nome, nivel }) => ({ perfilId, nome, nivel }))
  // Região ao vivo (`output`, papel status): o leitor de tela lê o aviso quando a lista chega.
  const avisos = (tela) => elementos(tela, (no) => no.localName === 'output' && no.textContent.includes('versão antiga')).map((no) => no.textContent)
  await naAba(SESSAO, { usuarios: [semAcoes], perfis: perfisAntigos }, async (tela) => {
    assert.deepEqual(avisos(tela), [AVISO_DE_VERSAO_ANTIGA])
    assert.deepEqual(botoesDe(tela), ['Convidar pessoa'], 'o bloqueio continua: nenhuma ação pela regra local')
  })
  await naAba(SESSAO, { usuarios: [pessoa()] }, async (tela) => {
    assert.deepEqual(avisos(tela), [], 'com a API nova, nenhum aviso')
  })
})

// ---------------------------------------------------------------- o que vai para a API

const { convidarUsuario, marcarDona } = await import('../src/infra/api/usuariosApi.js')

await conferirAssincrono('convite leva `dona` só quando é de dona; o resto do corpo não muda', async () => {
  api.chamadas.length = 0
  await convidarUsuario({ nome: ' Bia ', email: ' bia@exemplo.test ', perfilId: 'p-adm', dona: true })
  await convidarUsuario({ nome: 'Caio', email: 'caio@exemplo.test', perfilId: 'p-coz' })
  assert.deepEqual(escritas(), [
    { metodo: 'POST', url: '/api/usuarios', corpo: { empresaId: 'empresa-1', nome: 'Bia', email: 'bia@exemplo.test', senha: null, perfilId: 'p-adm', lojaId: null, dona: true } },
    { metodo: 'POST', url: '/api/usuarios', corpo: { empresaId: 'empresa-1', nome: 'Caio', email: 'caio@exemplo.test', senha: null, perfilId: 'p-coz', lojaId: null, dona: false } },
  ])
})

await conferirAssincrono('marcar e tirar a marca de dona: PUT na rota da dona, com o motivo só quando há', async () => {
  api.chamadas.length = 0
  await marcarDona('u-1', { dona: true, motivo: '  pedido da dona por telefone  ' })
  await marcarDona('u-2', { dona: false })
  await marcarDona('u-3', { dona: false, motivo: '   ' })
  assert.deepEqual(escritas(), [
    { metodo: 'PUT', url: '/api/usuarios/u-1/dona?empresaId=empresa-1', corpo: { dona: true, motivo: 'pedido da dona por telefone' } },
    { metodo: 'PUT', url: '/api/usuarios/u-2/dona?empresaId=empresa-1', corpo: { dona: false } },
    { metodo: 'PUT', url: '/api/usuarios/u-3/dona?empresaId=empresa-1', corpo: { dona: false } },
  ])
})

// ---------------------------------------------------------------- o que já valia (#1753)

conferir('desativada aparece como desativada mesmo com convite aberto', () => {
  assert.equal(situacaoDoUsuario({ usuarioId: 'bia', ativo: false, convite: { estado: 'pendente' } }).rotulo, 'Desativado')
  assert.equal(situacaoDoUsuario({ usuarioId: 'ana', ativo: true, convite: { estado: 'pendente' } }).rotulo, 'Convite enviado')
  assert.equal(situacaoDoUsuario({ usuarioId: 'dona', ativo: true, convite: { estado: 'nenhum' } }).rotulo, 'Ativo')
})

conferir('no filtro do histórico, nomes iguais ganham o e-mail', () => {
  const opcoes = opcoesDePessoas([
    { usuarioId: '1', nome: 'Ana', email: 'ana@casa.com' }, { usuarioId: '2', nome: 'Baba', email: 'baba@casa.com' },
    { usuarioId: '3', nome: 'ana', email: 'ana2@casa.com' },
  ])
  assert.deepEqual(opcoes.map((o) => o.rotulo), ['ana (ana2@casa.com)', 'Ana (ana@casa.com)', 'Baba'])
  assert.deepEqual(opcoes.find((o) => o.rotulo === 'Baba'), { valor: '2', rotulo: 'Baba' })
})

conferir('página nova não repete linha que já está na lista', () => {
  const juntas = juntarPaginas([{ id: 'a' }, { id: 'b' }], [{ id: 'b' }, { id: 'c' }])
  assert.deepEqual(juntas.map((l) => l.id), ['a', 'b', 'c'])
})

conferir('a senha segue a mesma política da API', () => {
  assert.equal(senhaValida('casadababa'), false)
  assert.deepEqual(regrasDaSenha('casadababa').filter((r) => !r.ok).map((r) => r.id), ['maiuscula', 'numero', 'especial'])
  assert.equal(senhaValida('Casa-da-Baba1'), true)
})

conferir('o token do convite vem do fragmento e a rota é reconhecida sem ele', () => {
  assert.equal(tokenDoConvite('#/convite?t=abc.DEF-1'), 'abc.DEF-1')
  assert.equal(tokenDoConvite('#/convite'), null)
  assert.equal(tokenDoConvite('#/m/configuracoes/usuarios'), null)
  assert.equal(ehRotaDeConvite('#/convite'), true)
  assert.equal(ehRotaDeConvite('#/convites'), false)
})

conferir('data da API sem fuso é lida como UTC', () => {
  assert.equal(instanteDaApi('2026-10-13T20:38:39.123'), Date.UTC(2026, 9, 13, 20, 38, 39, 123))
  assert.equal(instanteDaApi('2026-10-13T20:38:39Z'), Date.UTC(2026, 9, 13, 20, 38, 39))
  assert.equal(instanteDaApi(null), null)
})

const auditoria = (acao, quando, usuarioId = 'ana', detalhes = null) =>
  linhaDeAuditoria({ id: acao + quando, acao, dataHora: quando, usuarioId, usuarioNome: usuarioId, detalhes, sucesso: true })

conferir('a linha conta o fato inteiro e o jargão antigo some', () => {
  assert.equal(auditoria('perfil-trocado', 'x', 'ana', 'Perfil trocado de Cozinha para Atendimento por Baba').oQue,
    'Perfil trocado de Cozinha para Atendimento por Baba')
  assert.equal(auditoria('convite-reenviado', 'x', 'ana', 'Convite reenviado por Baba (Admin); WhatsApp: nao').oQue, 'Convite reenviado por Baba')
  assert.equal(auditoria('convite-reenviado', 'x', 'ana', 'Convite reenviado por Baba (Admin); WhatsApp: sim').oQue,
    'Convite reenviado por Baba, também pelo WhatsApp')
  assert.equal(auditoria('login', 'x', 'ana', 'Login realizado com sucesso').oQue, 'Entrou')
  assert.equal(auditoria('login_novo_dispositivo', 'x', 'ana', 'Login detectado em novo dispositivo. 1 sessão(ões) anterior(es) encerrada(s).').oQue,
    'Entrou em outro aparelho')
  assert.equal(auditoria('acao-nova_da-api', 'x').oQue, 'acao nova da api')
})

conferir('detalhe sem autor é texto de máquina: vale o rótulo da ação', () => {
  assert.equal(auditoria('aceitar-convite', 'x', 'ana', 'Convite aceito por e-mail; senha definida e canal verificado.').oQue,
    'Aceitou o convite e criou a senha')
  assert.equal(auditoria('logout', 'x', 'ana', 'Logout realizado com sucesso').oQue, 'Saiu')
  assert.equal(auditoria('reset-password', 'x', 'ana', 'Senha redefinida. 2 refresh tokens revogados.').oQue, 'Criou uma senha nova pelo link do e-mail')
  assert.equal(auditoria('forgot-password', 'x', 'ana', 'Link de redefinicao gerado').oQue, 'Pediu para criar uma senha nova')
  assert.equal(auditoria('admin-troca-email-solicitada', 'x', 'ana',
    'Troca de e-mail solicitada por 0b1e (Admin); espera confirmacao no endereco novo').oQue, 'Troca de e-mail pedida')
  assert.equal(auditoria('email-alterado', 'x', 'ana', 'Novo email confirmado e aplicado a conta').oQue, 'E-mail trocado')
  assert.equal(auditoria('telefone-verificado', 'x', 'ana', 'Telefone verificado pelo superadmin x. Motivo: y').oQue, 'Telefone confirmado')
  assert.equal(auditoria('usuario-desativado', 'x', 'ana', 'Acesso desativado por Baba').oQue, 'Acesso desativado por Baba')
  assert.equal(auditoria('acao-nova', 'x', 'ana', 'Texto qualquer da API').oQue, 'acao nova')
})

conferir('entrada e aviso do mesmo login viram uma linha; entradas seguidas viram contagem', () => {
  const linhas = [
    auditoria('login_novo_dispositivo', '2026-10-13T20:50:01'), auditoria('login', '2026-10-13T20:50:00'),
    auditoria('login', '2026-10-13T20:48:00'), auditoria('login', '2026-10-13T20:40:00'),
    auditoria('perfil-trocado', '2026-10-13T20:30:00', 'ana', 'Perfil trocado de Cozinha para Atendimento por Baba'),
    auditoria('login', '2026-10-13T20:20:00'), auditoria('login', '2026-10-13T20:10:00', 'bia'),
  ]
  const condensadas = condensarAuditoria(linhas)
  assert.deepEqual(condensadas.map((l) => l.oQue), [
    'Entrou 3 vezes', 'Perfil trocado de Cozinha para Atendimento por Baba', 'Entrou', 'Entrou',
  ])
  assert.equal(condensadas[0].primeira, '2026-10-13T20:40:00')
  assert.equal(linhas.length, 7, 'a lista original não é alterada')
  assert.equal(linhas[0].vezes, undefined)
})

conferir('entradas de dias diferentes não se juntam', () => {
  const linhas = [auditoria('login', '2026-10-13T03:10:00'), auditoria('login', '2026-10-13T02:50:00')]
  assert.equal(condensarAuditoria(linhas, () => false).length, 2)
})

conferir('o histórico agrupa por dia na ordem em que chegou', () => {
  const grupos = agruparPorDia([{ quando: 'a1' }, { quando: 'a2' }, { quando: 'b1' }], (q) => q[0])
  assert.deepEqual(grupos.map((g) => [g.dia, g.linhas.length]), [['a', 2], ['b', 1]])
})

conferir('reativar quem nunca entrou avisa que o convite antigo caiu', () => {
  assert.match(fraseDeRetorno('reativar', { nome: 'Eva', convite: { estado: 'pendente' } }), /Reenviar convite/)
  assert.equal(fraseDeRetorno('reativar', { nome: 'Eva', convite: { estado: 'aceito' } }), 'Eva pode entrar de novo.')
})

conferir('toda saída do convite tem título e o que fazer, e a página sem link não diz que o convite venceu', () => {
  for (const fase of ['pronto', 'vencido', 'semToken', 'limitado']) {
    assert.ok(SAIDAS_DO_CONVITE[fase].titulo && SAIDAS_DO_CONVITE[fase].texto, fase)
  }
  assert.doesNotMatch(SAIDAS_DO_CONVITE.semToken.titulo + SAIDAS_DO_CONVITE.semToken.texto, /não vale|reenviar/)
  assert.equal(SAIDAS_DO_CONVITE.pedindo, undefined)
})

conferir('as saídas do convite servem a quem entra pelo Google e à própria dona: sem "criou a senha" nem "peça à dona"', () => {
  assert.deepEqual(SAIDAS_DO_CONVITE.vencido, {
    titulo: 'Este convite já não vale',
    texto: 'Se você já entrou antes, use Entrar, com o Google ou com a senha. Se ainda não entrou, peça um convite novo a quem convidou você.',
  })
  assert.equal(SAIDAS_DO_CONVITE.semToken.texto, 'Use o botão do e-mail que você recebeu. Se você já entrou antes, é só usar Entrar.')
  assert.equal(SAIDAS_DO_CONVITE.limitado.texto, 'Espere uns minutos e abra o link do e-mail de novo. Se você já entrou antes, é só usar Entrar.')
  for (const fase of ['vencido', 'semToken', 'limitado']) assert.doesNotMatch(SAIDAS_DO_CONVITE[fase].texto, /dona|criou a sua senha/, fase)
})

console.log(`\n${passaram} verificações passaram${falharam.length ? `, ${falharam.length} falharam` : ''}.`)
process.exit(falharam.length ? 1 : 0)
