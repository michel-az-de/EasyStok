// Executa os formulários reais com hooks controlados e API na fronteira.
// Validação nativa, foco e pixels continuam sendo conferidos no navegador.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { transformWithOxc } from 'vite'

const url = new URL('../src/features/gestao/atendimento/AbaAtendimento.jsx', import.meta.url)
const fontes = new Map()
for (const caminho of ['gestao/atendimento/AbaAtendimento', 'automacoes/ModalAutomacoes', 'automacoes/ModalHorariosDaSemana']) {
  const arquivo = new URL(`../src/features/${caminho}.jsx`, import.meta.url)
  fontes.set(arquivo.href, (await transformWithOxc(readFileSync(arquivo, 'utf8'), arquivo.pathname, { jsx: { runtime: 'automatic' } })).code)
}
let ativo
const iguais = (a, b) => a && b && a.length === b.length && a.every((v, i) => Object.is(v, b[i]))
const hooks = {
  createContext: (inicial) => ({ inicial }),
  useContext: (contexto) => g.contexto ?? contexto.inicial,
  useState(inicial) {
    const h = ativo, i = h.i++
    if (!(i in h.valores)) h.valores[i] = typeof inicial === 'function' ? inicial() : inicial
    return [h.valores[i], (valor) => { h.valores[i] = typeof valor === 'function' ? valor(h.valores[i]) : valor; h.sujo = true }]
  },
  useRef(inicial) { return ativo.valores[ativo.i++] ??= { current: inicial } },
  useId() { return ativo.valores[ativo.i++] ??= `prova-${ativo.i}` },
  useCallback(fn, deps) {
    const h = ativo, i = h.i++, anterior = h.valores[i]
    if (!anterior || !iguais(anterior.deps, deps)) h.valores[i] = { fn, deps }
    return h.valores[i].fn
  },
  useEffect(fn, deps) {
    const h = ativo, i = h.i++, anterior = h.efeitos.get(i)
    if (!anterior || !iguais(anterior.deps, deps)) h.pendentes.push(() => { anterior?.limpar?.(); h.efeitos.set(i, { deps, limpar: fn() }) })
  },
}
class Tela {
  constructor(componente, props = {}) {
    this.componente = componente; this.props = props; this.valores = []; this.efeitos = new Map(); this.sujo = true
    this.renderizar()
  }
  renderizar() {
    let voltas = 0
    do {
      assert.ok(voltas++ < 20, 'sem ciclo de renderização')
      if (g.gravacoes && g.gravacoes !== this) {
        g.gravacoes.renderizar()
        g.contexto = { rascunhos: Object.fromEntries(g.rascunhos), definir: g.definir, ...g.gravacoes.arvore }
      }
      ativo = this; this.i = 0; this.pendentes = []; this.sujo = false
      this.arvore = this.componente(this.props)
      this.pendentes.forEach((fn) => fn())
    } while (this.sujo)
    return this.arvore
  }
  async atualizar() {
    for (let i = 0; i < 12; i++) { await Promise.resolve(); this.renderizar() }
    return this.arvore
  }
  desmontar() { this.efeitos.forEach((efeito) => efeito.limpar?.()) }
}
const g = globalThis.__config1653 = { hooks, acoes: {}, estado: {}, rascunhos: new Map(), setters: new Map() }
g.rascunho = (chave) => {
  if (!g.setters.has(chave)) g.setters.set(chave, (valor) => {
    const atual = g.rascunhos.get(chave) ?? null
    const proximo = typeof valor === 'function' ? valor(atual) : valor
    if (proximo === null) g.rascunhos.delete(chave)
    else g.rascunhos.set(chave, proximo)
  })
  return [g.rascunhos.get(chave) ?? null, g.setters.get(chave)]
}
g.definir = (chave, valor) => g.rascunho(chave)[1](valor)
const inline = (source) => ({ format: 'module', shortCircuit: true, source })
registerHooks({
  resolve(spec, ctx, next) {
    if (spec === 'react') return { url: 'data:text/javascript,export const {createContext,useContext,useState,useRef,useCallback,useEffect,useId}=globalThis.__config1653.hooks', shortCircuit: true }
    if (spec === 'react/jsx-runtime') return { url: 'data:text/javascript,export const jsx=(type,props)=>({type,props});export const jsxs=jsx;export const Fragment="fragment"', shortCircuit: true }
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      for (const ext of ['.js', '.jsx']) { try { return next(spec + ext, ctx) } catch { /* próxima extensão */ } }
    }
    return next(spec, ctx)
  },
  load(caminho, ctx, next) {
    if (fontes.has(caminho)) return inline(fontes.get(caminho))
    if (caminho.endsWith('/aplicacao/contextos.js')) return inline('export const useAcoes=()=>globalThis.__config1653.acoes;export const useAtendimento=()=>globalThis.__config1653.estado;export const useCatalogo=()=>({})')
    if (caminho.endsWith('/aplicacao/acessoModulos.jsx')) return inline('export const useAcessoModulos=()=>({acoes:{editarAtendimento:true,controlarLoja:true}})')
    if (caminho.endsWith('/infra/api/cliente.js')) return inline('export const chamarApi=(...args)=>globalThis.__config1653.chamarApi(...args)')
    if (caminho.endsWith('/infra/api/sessao.js')) return inline('export const lerSessao=()=>({empresa:{id:"empresa"},usuario:{id:"pessoa"}});export const identidadeDaSessao=(s)=>s.empresa.id+":"+s.usuario.id')
    if (caminho.endsWith('.css')) return inline('export default new Proxy({},{get:(_,p)=>p})')
    if (caminho.endsWith('.jsx')) return inline([...readFileSync(new URL(caminho), 'utf8').matchAll(/^export (?:function|const) (\w+)/gm)].map((m) => `export const ${m[1]}='${m[1]}'`).join('\n'))
    return next(caminho, ctx)
  },
})
const { AbaAtendimento } = await import(url.href)
const { ModalHorariosDaSemana } = await import('../src/features/automacoes/ModalHorariosDaSemana.jsx')
const { criarAcoesExpedienteApi } = await import('../src/aplicacao/api/expediente.js')
const { FUNCIONAMENTO_PADRAO } = await import('../src/dominio/funcionamento.js')
const { useGravacoesDosAjustes } = await import('../src/aplicacao/rascunhosDosAjustes.js')
const semana = () => structuredClone(FUNCIONAMENTO_PADRAO)
const expandir = (el) => typeof el?.type === 'function' && ['Pendencia', 'Retorno', 'DiaDeFuncionamento'].includes(el.type.name) ? el.type(el.props) : el
const andar = (el) => {
  if (!el || typeof el !== 'object') return []
  if (Array.isArray(el)) return el.flatMap(andar)
  const no = expandir(el)
  return no ? [no, ...andar(no.props?.children), ...andar(no.props?.rodape)] : []
}
const texto = (el) => typeof el !== 'object' || el === null ? String(el ?? '') : Array.isArray(el) ? el.map(texto).join('') : texto(expandir(el)?.props?.children)
const campo = (tela, rotulo) => andar(tela.arvore).find((el) => el.props?.rotulo === rotulo)
const botao = (tela, nome) => andar(tela.arvore).find((el) => el.type === 'Botao' && texto(el) === nome)
const mudar = (tela, rotulo, valor) => { campo(tela, rotulo).props.onChange({ target: { value: valor, setCustomValidity() {} } }); tela.renderizar() }
const pendente = (tela) => texto(tela.arvore).includes('Alterações não salvas')
const adiado = () => { let resolver; return { promessa: new Promise((r) => { resolver = r }), resolver: (v) => resolver(v) } }
const padrao = () => ({ ativo: true, tom: 'acolhedor', nivelSugestao: 'Discreto', saudacaoPrimeiroContato: 'Olá', saudacaoRetorno: 'Olá de novo', fraseEspera: 'Só um instante', mensagemForaArea: 'Fora da área', modeloRetomadaNome: '', respiroMinutos: 40, tempoPreparoPadraoMinutos: 60, slaRespostaMinutos: 5 })
let atual, escritas, leituras, carregar, salvar, salvarMensagens, salvarHorarios, recarregarExpediente
function preparar() {
  g.rascunhos.clear(); atual = padrao(); escritas = []; leituras = 0
  g.contexto = null; g.gravacoes = null; g.gravacoes = new Tela(useGravacoesDosAjustes)
  g.estado = { agora: Date.parse('2026-10-12T12:00:00-03:00'), expediente: { carregado: true, mensagemForaDoHorario: 'Voltamos amanhã', mensagemLojaFechada: 'Fechada por hoje' }, lojaAberta: null, aberta: true }
  carregar = async () => ({ ...atual })
  salvar = async (dados) => { atual = { ...dados }; return atual }
  salvarMensagens = async (dados) => { g.estado.expediente = { ...g.estado.expediente, ...dados } }
  salvarHorarios = async (funcionamento) => ({ funcionamento })
  recarregarExpediente = () => {}
  g.acoes = {
    carregarConfiguracao: async () => { leituras++; return carregar() },
    salvarConfiguracao: async (dados) => { escritas.push(dados); return salvar(dados) },
    salvarMensagensExpediente: async (dados) => { escritas.push(dados); return salvarMensagens(dados) },
    salvarHorariosExpediente: async (dados) => { escritas.push(dados); return salvarHorarios(dados) },
    recarregarExpediente: () => recarregarExpediente(), alternarLoja() {}, voltarAoHorario() {},
  }
}
function montar(nome, props) {
  const aba = new Tela(AbaAtendimento, props)
  const no = andar(aba.arvore).find((el) => el.type?.name === nome)
  return new Tela(no.type, no.props)
}
const enviar = (tela, valido = true) => andar(tela.arvore).find((el) => el.type === 'form').props.onSubmit({ preventDefault() {}, currentTarget: {
  querySelectorAll: () => [], reportValidity: () => valido,
} })
let passou = 0
async function provar(nome, fn) { preparar(); await fn(); passou++; console.log('ok ' + nome) }

await provar('editar e desfazer o automático não altera a loja antes de salvar', async () => {
  const tela = montar('ConfiguracaoDoAtendimento'); await tela.atualizar()
  assert.equal(botao(tela, 'Salvar configuração').props.disabled, true)
  andar(tela.arvore).find((el) => el.type === 'input' && el.props.type === 'checkbox').props.onChange({ target: { checked: false } })
  tela.renderizar()
  assert.equal(pendente(tela), true); assert.equal(escritas.length, 0)
  assert.match(texto(tela.arvore), /A mudança só vale depois de salvar/)
  botao(tela, 'Desfazer edição da configuração').props.onClick(); await tela.atualizar()
  assert.equal(pendente(tela), false); assert.equal(g.rascunhos.size, 0)
})
await provar('navegar e voltar conserva o texto em memória, incluindo espaço para continuar escrevendo', async () => {
  const tela = montar('ConfiguracaoDoAtendimento'); await tela.atualizar()
  mudar(tela, 'Tom', 'acolhedor ')
  assert.equal(campo(tela, 'Tom').props.value, 'acolhedor ')
  tela.desmontar()
  const volta = montar('ConfiguracaoDoAtendimento'); await volta.atualizar()
  assert.equal(campo(volta, 'Tom').props.value, 'acolhedor '); assert.equal(pendente(volta), true)
})
await provar('formulário inválido não envia e os limites reais estão nos campos', async () => {
  const tela = montar('ConfiguracaoDoAtendimento'); await tela.atualizar(); mudar(tela, 'Preparo padrão (min)', '')
  await enviar(tela, false)
  assert.equal(escritas.length, 0)
  for (const nome of ['Tom', 'Saudação no primeiro contato', 'Saudação para quem volta', 'Frase de espera', 'Mensagem para endereço fora da área', 'Tempo extra de preparo (min)', 'Preparo padrão (min)']) assert.equal(campo(tela, nome).props.required, true)
  assert.equal(campo(tela, 'Modelo de retomada (WhatsApp)').props.pattern, '[a-z0-9_]+')
  assert.equal(campo(tela, 'Modelo de retomada (WhatsApp)').props.maxLength, 512)
})
await provar('falha de gravação preserva a edição e permite tentar novamente', async () => {
  const tela = montar('ConfiguracaoDoAtendimento'); await tela.atualizar(); mudar(tela, 'Tom', 'direto')
  salvar = async () => { throw new Error('Sem conexão com o EasyStok.') }
  await enviar(tela); await tela.atualizar()
  assert.equal(campo(tela, 'Tom').props.value, 'direto'); assert.equal(pendente(tela), true)
  assert.match(texto(tela.arvore), /Sem conexão/); assert.equal(botao(tela, 'Salvar configuração').props.disabled, false)
})
await provar('texto só com espaços recebe orientação antes de qualquer gravação', async () => {
  const tela = montar('ConfiguracaoDoAtendimento'); await tela.atualizar(); mudar(tela, 'Tom', '   ')
  let mensagem = ''
  await tela.arvore.props.onSubmit({ preventDefault() {}, currentTarget: {
    querySelectorAll: () => [{ value: '   ', setCustomValidity(valor) { mensagem = valor } }],
    reportValidity: () => !mensagem,
  } })
  assert.match(mensagem, /Preencha este campo/); assert.equal(escritas.length, 0)
  let limpou = false
  campo(tela, 'Tom').props.onChange({ target: { value: 'direto', setCustomValidity(valor) { limpou = valor === '' } } })
  assert.equal(limpou, true)
})
await provar('falha parcial explica o que gravou e desfazer relê o estado confirmado', async () => {
  const tela = montar('ConfiguracaoDoAtendimento'); await tela.atualizar(); mudar(tela, 'Tom', 'direto')
  salvar = async (dados) => { atual = { ...dados }; throw new Error('Os demais ajustes foram salvos. O modelo de retomada não salvou: sem conexão.') }
  await enviar(tela); await tela.atualizar()
  assert.match(texto(tela.arvore), /Os demais ajustes foram salvos/); assert.equal(g.rascunhos.size, 1)
  botao(tela, 'Desfazer edição da configuração').props.onClick(); await tela.atualizar()
  assert.equal(campo(tela, 'Tom').props.value, 'direto'); assert.equal(g.rascunhos.size, 0)
  assert.ok(leituras > 1)
})
await provar('resposta sem confirmação não exibe Salvo nem apaga o rascunho', async () => {
  const tela = montar('ConfiguracaoDoAtendimento'); await tela.atualizar(); mudar(tela, 'Tom', 'direto')
  salvar = async () => null
  await enviar(tela); await tela.atualizar()
  assert.match(texto(tela.arvore), /não confirmou/); assert.doesNotMatch(texto(tela.arvore), /Salvo no EasyStok/)
  assert.equal(g.rascunhos.size, 1)
})
await provar('sucesso usa a resposta confirmada e desativa gravação repetida', async () => {
  const tela = montar('ConfiguracaoDoAtendimento'); await tela.atualizar(); mudar(tela, 'Tom', ' direto ')
  salvar = async (dados) => ({ ...dados, tom: dados.tom.trim() })
  await enviar(tela); await tela.atualizar()
  assert.equal(campo(tela, 'Tom').props.value, 'direto'); assert.equal(g.rascunhos.size, 0)
  assert.match(texto(tela.arvore), /Salvo no EasyStok/); assert.equal(botao(tela, 'Salvar configuração').props.disabled, true)
})
await provar('configuração: voltar durante PUT permite editar, bloqueia outro envio e atualiza o baseline', async () => {
  const tela = montar('ConfiguracaoDoAtendimento'); await tela.atualizar(); mudar(tela, 'Tom', 'direto')
  const espera = adiado(); salvar = async (dados) => { await espera.promessa; atual = { ...dados }; return atual }
  const envio = enviar(tela); tela.desmontar()
  const volta = montar('ConfiguracaoDoAtendimento'); await volta.atualizar(); mudar(volta, 'Tom', 'sem gírias')
  assert.equal(botao(volta, 'Salvar configuração').props.disabled, true)
  assert.match(texto(volta.arvore), /Outro ajuste está sendo salvo/)
  await enviar(volta); assert.equal(escritas.length, 1)
  espera.resolver(); await envio; await volta.atualizar()
  assert.equal(campo(volta, 'Tom').props.value, 'sem gírias'); assert.equal(pendente(volta), true)
  assert.equal(botao(volta, 'Salvar configuração').props.disabled, false)
  botao(volta, 'Desfazer edição da configuração').props.onClick(); await volta.atualizar()
  assert.equal(campo(volta, 'Tom').props.value, 'direto')
  mudar(volta, 'Tom', 'sem gírias'); await enviar(volta); await volta.atualizar()
  assert.equal(atual.tom, 'sem gírias'); assert.equal(escritas.length, 2)
})
await provar('sucesso após voltar à tela atualiza o valor confirmado', async () => {
  const tela = montar('ConfiguracaoDoAtendimento'); await tela.atualizar(); mudar(tela, 'Tom', 'direto')
  const espera = adiado(); salvar = () => espera.promessa
  const envio = enviar(tela); tela.desmontar()
  const volta = montar('ConfiguracaoDoAtendimento'); await volta.atualizar()
  atual = { ...padrao(), tom: 'direto' }; espera.resolver(atual); await envio; await volta.atualizar()
  assert.equal(campo(volta, 'Tom').props.value, 'direto'); assert.equal(pendente(volta), false)
})
await provar('GET iniciado ao desfazer não sobrescreve um PUT confirmado depois', async () => {
  const tela = montar('ConfiguracaoDoAtendimento'); await tela.atualizar(); mudar(tela, 'Tom', 'temporário')
  const espera = adiado(), antigo = { ...atual }; carregar = () => espera.promessa
  botao(tela, 'Desfazer edição da configuração').props.onClick(); await tela.atualizar()
  mudar(tela, 'Tom', 'direto'); await enviar(tela); await tela.atualizar()
  espera.resolver(antigo); await tela.atualizar()
  assert.equal(campo(tela, 'Tom').props.value, 'direto'); assert.equal(g.rascunhos.size, 0)
  assert.equal(botao(tela, 'Salvar configuração').props.disabled, true)
})
await provar('mensagens do expediente conservam a edição na falha e limpam após confirmação', async () => {
  const tela = montar('MensagensDoExpediente'); mudar(tela, 'Fora do horário', 'Voltamos às 10h')
  salvarMensagens = async () => { throw new Error('Conexão perdida') }
  await enviar(tela); await tela.atualizar(); assert.equal(pendente(tela), true)
  tela.desmontar(); const volta = montar('MensagensDoExpediente')
  assert.equal(campo(volta, 'Fora do horário').props.value, 'Voltamos às 10h')
  salvarMensagens = async (dados) => { g.estado.expediente = { ...g.estado.expediente, ...dados } }
  await enviar(volta); await volta.atualizar()
  assert.equal(pendente(volta), false); assert.equal(g.rascunhos.size, 0)
})
await provar('desfazer mensagens preserva o outro formulário e não deixa pendência falsa', async () => {
  const mensagens = montar('MensagensDoExpediente')
  const config = montar('ConfiguracaoDoAtendimento'); await config.atualizar()
  mudar(mensagens, 'Fora do horário', 'Voltamos às 10h'); mudar(config, 'Tom', 'direto')
  assert.equal(g.rascunhos.size, 2)
  botao(mensagens, 'Desfazer edição das mensagens').props.onClick(); await mensagens.atualizar()
  assert.equal(g.rascunhos.size, 1); assert.equal(g.rascunhos.get('configuracao-atendimento').tom, 'direto')
  mudar(mensagens, 'Fora do horário', 'Voltamos às 10h')
  g.estado.expediente.mensagemForaDoHorario = 'Voltamos às 10h'
  await mensagens.atualizar()
  assert.equal(g.rascunhos.size, 1); assert.equal(pendente(mensagens), false)
})
await provar('resposta tardia das mensagens recarrega o expediente da nova tela', async () => {
  const tela = montar('MensagensDoExpediente'); mudar(tela, 'Fora do horário', 'Voltamos às 10h')
  const espera = adiado(), providerAnterior = g.estado
  salvarMensagens = async (dados) => { await espera.promessa; providerAnterior.expediente = { ...providerAnterior.expediente, ...dados } }
  const envio = enviar(tela); tela.desmontar()
  g.estado = { ...providerAnterior, expediente: { ...providerAnterior.expediente } }
  const volta = montar('MensagensDoExpediente')
  recarregarExpediente = () => { g.estado.expediente = { ...providerAnterior.expediente } }
  espera.resolver(); await envio; await volta.atualizar()
  assert.equal(campo(volta, 'Fora do horário').props.value, 'Voltamos às 10h'); assert.equal(pendente(volta), false)
})
await provar('mensagens: nova edição aguarda PUT anterior, depois desfazer mostra o confirmado', async () => {
  const tela = montar('MensagensDoExpediente'); mudar(tela, 'Fora do horário', 'Voltamos às 10h')
  const espera = adiado(), providerAnterior = g.estado
  salvarMensagens = async (dados) => { await espera.promessa; providerAnterior.expediente = { ...providerAnterior.expediente, ...dados } }
  const envio = enviar(tela); tela.desmontar()
  g.estado = { ...providerAnterior, expediente: { ...providerAnterior.expediente } }
  const volta = montar('MensagensDoExpediente'); mudar(volta, 'Fora do horário', 'Voltamos às 11h')
  assert.equal(botao(volta, 'Salvar mensagens').props.disabled, true)
  assert.equal(campo(volta, 'Fora do horário').props.disabled, false)
  await enviar(volta); assert.equal(escritas.length, 1)
  recarregarExpediente = () => { g.estado.expediente = { ...providerAnterior.expediente } }
  espera.resolver(); await envio; await volta.atualizar()
  assert.equal(campo(volta, 'Fora do horário').props.value, 'Voltamos às 11h')
  assert.equal(botao(volta, 'Salvar mensagens').props.disabled, false)
  botao(volta, 'Desfazer edição das mensagens').props.onClick(); await volta.atualizar()
  assert.equal(campo(volta, 'Fora do horário').props.value, 'Voltamos às 10h')
})
await provar('horários chegam por composição na seção da loja', () => {
  const slot = { type: 'button', props: { children: 'Ajustar horários' } }
  const tela = montar('LojaAgora', { horariosAtendimento: slot })
  assert.ok(andar(tela.arvore).includes(slot)); assert.doesNotMatch(texto(tela.arvore), /Balcão, botão/)
})
await provar('horários: editar e fechar conserva draft sem disparar autosave', async () => {
  recarregarExpediente = async () => ({ funcionamento: semana() })
  let fechou = 0
  const tela = new Tela(ModalHorariosDaSemana, { aoFechar: () => { fechou++ } }); await tela.atualizar()
  mudar(tela, 'Abre, Segunda', '09:00'); botao(tela, 'Fechar').props.onClick()
  assert.equal(fechou, 1); assert.equal(escritas.length, 0); assert.equal(g.rascunhos.get('horarios-atendimento').segunda.abre, '09:00')
  tela.desmontar(); const volta = new Tela(ModalHorariosDaSemana, { aoFechar() {} }); await volta.atualizar()
  assert.equal(campo(volta, 'Abre, Segunda').props.value, '09:00')
  botao(volta, 'Desfazer edição').props.onClick(); await volta.atualizar()
  assert.equal(campo(volta, 'Abre, Segunda').props.value, '08:00'); assert.equal(g.rascunhos.size, 0)
})
await provar('horários: gravação pendente bloqueia fechar, desfazer e envio duplicado', async () => {
  recarregarExpediente = async () => ({ funcionamento: semana() })
  let fechou = 0
  const tela = new Tela(ModalHorariosDaSemana, { aoFechar: () => { fechou++ } }); await tela.atualizar()
  mudar(tela, 'Abre, Segunda', '09:00')
  const fecharAntigo = tela.arvore.props.aoFechar, espera = adiado()
  salvarHorarios = () => espera.promessa
  const envio = enviar(tela); await tela.atualizar(); fecharAntigo(); await enviar(tela)
  assert.equal(fechou, 0); assert.equal(tela.arvore.props.aoFechar, undefined); assert.equal(escritas.length, 1)
  assert.equal(botao(tela, 'Fechar').props.disabled, true); assert.equal(botao(tela, 'Desfazer edição').props.disabled, true)
  assert.match(texto(tela.arvore), /Sair não cancela o que já foi enviado/)
  espera.resolver({ funcionamento: escritas[0] }); await envio; await tela.atualizar()
  assert.match(texto(tela.arvore), /Horários salvos no EasyStok/); assert.equal(g.rascunhos.size, 0)
})
await provar('horários: campo vazio e falha HTTP preservam a edição sem falso salvo', async () => {
  recarregarExpediente = async () => ({ funcionamento: semana() })
  const tela = new Tela(ModalHorariosDaSemana, { aoFechar() {} }); await tela.atualizar()
  mudar(tela, 'Abre, Segunda', '')
  await enviar(tela, false); assert.equal(escritas.length, 0); assert.equal(campo(tela, 'Abre, Segunda').props.required, true)
  mudar(tela, 'Abre, Segunda', '09:00'); salvarHorarios = async () => { throw new Error('Sem conexão') }
  await enviar(tela); await tela.atualizar()
  assert.match(texto(tela.arvore), /Sem conexão/); assert.doesNotMatch(texto(tela.arvore), /Horários salvos no EasyStok/)
  assert.equal(g.rascunhos.size, 1); assert.equal(botao(tela, 'Fechar').props.disabled, false)
})
await provar('horários: nova edição aguarda o modal anterior, preserva B e desfazer mostra A', async () => {
  let funcionamento = semana()
  recarregarExpediente = async () => ({ funcionamento })
  const tela = new Tela(ModalHorariosDaSemana, { aoFechar() {} }); await tela.atualizar(); mudar(tela, 'Abre, Segunda', '09:00')
  const espera = adiado(); salvarHorarios = async (dados) => { await espera.promessa; funcionamento = dados; return { funcionamento } }
  const envio = enviar(tela); tela.desmontar()
  const volta = new Tela(ModalHorariosDaSemana, { aoFechar() {} }); await volta.atualizar(); mudar(volta, 'Abre, Segunda', '10:00')
  assert.equal(botao(volta, 'Salvar horários').props.disabled, true)
  assert.match(texto(volta.arvore), /Outro ajuste está sendo salvo/)
  await enviar(volta); assert.equal(escritas.length, 1)
  espera.resolver(); await envio; await volta.atualizar()
  assert.equal(campo(volta, 'Abre, Segunda').props.value, '10:00'); assert.equal(g.rascunhos.size, 1)
  assert.equal(botao(volta, 'Salvar horários').props.disabled, false)
  botao(volta, 'Desfazer edição').props.onClick(); await volta.atualizar()
  assert.equal(campo(volta, 'Abre, Segunda').props.value, '09:00')
  mudar(volta, 'Abre, Segunda', '10:00'); await enviar(volta); await volta.atualizar()
  assert.equal(funcionamento.segunda.abre, '10:00'); assert.equal(escritas.length, 2)
})
await provar('a trava é síncrona, idempotente, independente por recurso e isolada por visita', () => {
  const visita = g.gravacoes.arvore
  const liberar = visita.iniciarGravacao('expediente-atendimento')
  assert.equal(typeof liberar, 'function')
  assert.equal(visita.iniciarGravacao('expediente-atendimento'), null)
  const liberarConfig = visita.iniciarGravacao('configuracao-atendimento')
  assert.equal(typeof liberarConfig, 'function')
  liberar(); const novo = visita.iniciarGravacao('expediente-atendimento'); liberar()
  assert.equal(visita.estaGravando('expediente-atendimento'), true)
  novo(); liberarConfig()
  const outraVisita = new Tela(useGravacoesDosAjustes)
  const liberarOutra = outraVisita.arvore.iniciarGravacao('expediente-atendimento')
  assert.equal(visita.estaGravando('expediente-atendimento'), false); liberarOutra()
})
await provar('configuração: dupla entrada antes de renderizar mantém a confirmação da única escrita', async () => {
  const tela = montar('ConfiguracaoDoAtendimento'); await tela.atualizar(); mudar(tela, 'Tom', 'direto')
  const espera = adiado(); salvar = async (dados) => { await espera.promessa; atual = dados; return dados }
  const primeiro = enviar(tela), segundo = enviar(tela)
  assert.equal(escritas.length, 1)
  espera.resolver(); await Promise.all([primeiro, segundo]); await tela.atualizar()
  assert.equal(campo(tela, 'Tom').props.value, 'direto'); assert.equal(botao(tela, 'Salvar configuração').props.disabled, true)
})
await provar('mensagens e horários compartilham a gravação; configuração continua independente', async () => {
  let funcionamento = semana()
  recarregarExpediente = async () => ({ funcionamento })
  const mensagens = montar('MensagensDoExpediente'); mudar(mensagens, 'Fora do horário', 'Voltamos às 10h')
  const horarios = new Tela(ModalHorariosDaSemana, { aoFechar() {} }); await horarios.atualizar(); mudar(horarios, 'Abre, Segunda', '09:00')
  const config = montar('ConfiguracaoDoAtendimento'); await config.atualizar(); mudar(config, 'Tom', 'direto')
  const esperaMensagem = adiado(); salvarMensagens = async () => { await esperaMensagem.promessa; throw new Error('Conexão perdida') }
  const envioMensagem = enviar(mensagens); await horarios.atualizar(); await config.atualizar()
  assert.equal(botao(horarios, 'Salvar horários').props.disabled, true)
  assert.equal(botao(config, 'Salvar configuração').props.disabled, false)
  await enviar(horarios); assert.equal(escritas.length, 1)
  esperaMensagem.resolver(); await envioMensagem; await horarios.atualizar()
  assert.equal(botao(horarios, 'Salvar horários').props.disabled, false)
  const esperaHorario = adiado(); salvarHorarios = async (dados) => { await esperaHorario.promessa; funcionamento = dados; return { funcionamento } }
  const envioHorario = enviar(horarios); await mensagens.atualizar()
  assert.equal(botao(mensagens, 'Salvar mensagens').props.disabled, true)
  await enviar(mensagens); assert.equal(escritas.length, 2)
  esperaHorario.resolver(); await envioHorario; await mensagens.atualizar()
  assert.equal(botao(mensagens, 'Salvar mensagens').props.disabled, false)
})
await provar('configuração: voltar ao valor antigo durante gravação conserva essa nova escolha', async () => {
  const tela = montar('ConfiguracaoDoAtendimento'); await tela.atualizar(); mudar(tela, 'Tom', 'direto')
  const espera = adiado(); salvar = async (dados) => { await espera.promessa; atual = dados; return dados }
  const envio = enviar(tela); tela.desmontar()
  const volta = montar('ConfiguracaoDoAtendimento'); await volta.atualizar(); mudar(volta, 'Tom', 'acolhedor')
  volta.desmontar(); const outraVolta = montar('ConfiguracaoDoAtendimento'); await outraVolta.atualizar()
  espera.resolver(); await envio; await outraVolta.atualizar()
  assert.equal(campo(outraVolta, 'Tom').props.value, 'acolhedor'); assert.equal(pendente(outraVolta), true)
})
await provar('mensagens: voltar ao valor antigo durante gravação conserva essa nova escolha', async () => {
  const tela = montar('MensagensDoExpediente'); mudar(tela, 'Fora do horário', 'Voltamos às 10h')
  const espera = adiado(), providerAnterior = g.estado
  salvarMensagens = async (dados) => { await espera.promessa; providerAnterior.expediente = { ...providerAnterior.expediente, ...dados } }
  const envio = enviar(tela); tela.desmontar()
  g.estado = { ...providerAnterior, expediente: { ...providerAnterior.expediente } }
  const volta = montar('MensagensDoExpediente'); mudar(volta, 'Fora do horário', 'Voltamos amanhã')
  recarregarExpediente = () => { g.estado.expediente = { ...providerAnterior.expediente } }
  espera.resolver(); await envio; await volta.atualizar()
  assert.equal(campo(volta, 'Fora do horário').props.value, 'Voltamos amanhã'); assert.equal(pendente(volta), true)
})
await provar('horários: voltar ao valor antigo durante gravação conserva essa nova escolha', async () => {
  let funcionamento = semana(); recarregarExpediente = async () => ({ funcionamento })
  const tela = new Tela(ModalHorariosDaSemana, { aoFechar() {} }); await tela.atualizar(); mudar(tela, 'Abre, Segunda', '09:00')
  const espera = adiado(); salvarHorarios = async (dados) => { await espera.promessa; funcionamento = dados; return { funcionamento } }
  const envio = enviar(tela); tela.desmontar()
  const volta = new Tela(ModalHorariosDaSemana, { aoFechar() {} }); await volta.atualizar(); mudar(volta, 'Abre, Segunda', '08:00')
  volta.desmontar(); const outraVolta = new Tela(ModalHorariosDaSemana, { aoFechar() {} }); await outraVolta.atualizar()
  espera.resolver(); await envio; await outraVolta.atualizar()
  assert.equal(campo(outraVolta, 'Abre, Segunda').props.value, '08:00'); assert.equal(pendente(outraVolta), true)
})
await provar('ação horários: envia só semana e identidade; confirma estado antes de retornar', async () => {
  const chamadas = [], despachos = []
  g.chamarApi = async (caminho, opcoes) => { chamadas.push({ caminho, opcoes }); return { horarios: [{ diaDaSemana: 1, abre: '09:00:00', fecha: '18:00:00' }], controleManual: 'Automatico' } }
  const acoes = criarAcoesExpedienteApi({ despachar: (acao) => despachos.push(acao), estadoRef: { current: {} } })
  const confirmado = await acoes.salvarHorariosExpediente(semana())
  assert.equal(chamadas.length, 1); assert.equal(chamadas[0].opcoes.identidadeEsperada, 'empresa:pessoa')
  assert.equal(chamadas[0].opcoes.corpo.horarios.length, 7); assert.equal(chamadas[0].opcoes.corpo.mensagemForaDoHorario, undefined)
  assert.equal(confirmado.funcionamento.segunda.abre, '09:00'); assert.equal(confirmado.funcionamento.domingo.fechado, true)
  assert.equal(despachos[0].funcionamento.segunda.abre, '09:00')
})
await provar('ação horários: semana incompleta não envia e resposta vazia não sincroniza', async () => {
  const chamadas = [], despachos = []
  g.chamarApi = async (...args) => { chamadas.push(args); return null }
  const acoes = criarAcoesExpedienteApi({ despachar: (acao) => despachos.push(acao), estadoRef: { current: {} } })
  const incompleto = semana(); incompleto.segunda.abre = ''
  await assert.rejects(acoes.salvarHorariosExpediente(incompleto), /Preencha/); assert.equal(chamadas.length, 0)
  await assert.rejects(acoes.salvarHorariosExpediente(semana()), /não confirmou/); assert.equal(despachos.length, 0)
})
console.log(`\n${passou} verificações passaram.`)
