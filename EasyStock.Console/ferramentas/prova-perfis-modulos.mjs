import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { entradaDoPerfil, permiteModulo, permiteRota } from '../src/dominio/acessoModulos.js'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const cozinha = { portaDeEntrada: 'cozinha', modulos: [
  { id: 'cozinha', liberado: true }, { id: 'producao', liberado: true }, { id: 'financeiro', liberado: false },
] }
assert.equal(entradaDoPerfil(cozinha), '#/m/cozinha')
assert.equal(permiteRota(cozinha, { tipo: 'cozinha' }), true)
assert.equal(permiteRota(cozinha, { tipo: 'entregas' }), false)
assert.equal(permiteRota(cozinha, { tipo: 'principal', modulo: 'atendimento' }), false)
assert.equal(permiteRota(cozinha, { tipo: 'modulo', modulo: 'financeiro' }), false)
assert.equal(permiteRota(cozinha, { tipo: 'modulo', modulo: 'producao' }), true)
assert.equal(permiteRota(cozinha, { tipo: 'hall' }), true)
assert.equal(permiteModulo(null, 'cozinha'), false)
assert.equal(entradaDoPerfil({ ...cozinha, portaDeEntrada: 'financeiro' }), '#/')
assert.equal(permiteModulo({ modulos: [{ id: 'cozinha', liberado: 'true' }] }, 'cozinha'), false)
console.log('Perfis: entrada, links diretos, apelidos, hall e falha fechada aprovados.')
// #1921 e #1940 (item 1, frente C da #1889): a atendente via "Janelas de entrega" e o console pedia
// integrações, janelas e desacertos que a API recusa (403 a cada carga e a cada minuto).
const acesso = await import('../src/dominio/acessoModulos.js')
const { moduloPorId, telasDoMenu, modulosDoHall } = await import('../src/dominio/modulos.js')
const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8').replaceAll(String.fromCharCode(13), '')
const ATENDENTE = { editarAtendimento: false, controlarLoja: false, reabrirConversa: true }
const DONA = { editarAtendimento: true, controlarLoja: true, reabrirConversa: true }

assert.equal(acesso.podeVerIntegracoes(ATENDENTE), false)
assert.equal(acesso.podeAjustarEntrega(ATENDENTE), false)
assert.equal(acesso.podeVerIntegracoes(DONA), true)
assert.equal(acesso.podeAjustarEntrega(DONA), true)
assert.equal(acesso.podeVerIntegracoes({ ...DONA, verIntegracoes: false }), false, 'a chave do servidor vale sobre a da dona')
assert.equal(acesso.podeAjustarEntrega({ ...ATENDENTE, ajustarEntrega: true }), true)
assert.equal(acesso.podeVerDesacertos({ verDesacertosEstoque: false }), false)
assert.equal(acesso.podeVerDesacertos({ verDesacertosEstoque: true }), true)
assert.equal(acesso.podeVerDesacertos(ATENDENTE), true, 'sem a chave do servidor, lê como antes')
assert.equal(acesso.podeVerIntegracoes(null), false)

const entregas = moduloPorId('entregas')
const ids = (telas) => telas.map((t) => t.id)
assert.deepEqual(ids(telasDoMenu(entregas, { fonteApi: true, acoes: ATENDENTE })), ['painel', 'agenda'], 'a atendente não vê Janelas de entrega')
assert.deepEqual(ids(telasDoMenu(entregas, { fonteApi: true, acoes: DONA })), ['painel', 'agenda', 'janelas'])
assert.deepEqual(ids(telasDoMenu(entregas, { fonteApi: false, acoes: ATENDENTE })), ['painel', 'janelas'], 'na demonstração a matriz não decide')
assert.deepEqual(ids(telasDoMenu(entregas, { fonteApi: true })), ['painel', 'agenda', 'janelas'], 'sem a matriz (guia, rota), a tela fica')
assert.ok(!modulosDoHall({ fonteApi: true, acoes: ATENDENTE }).find((m) => m.id === 'entregas').telas.some((t) => t.id === 'janelas'))
for (const caminho of ['features/hall/MolduraDoModulo.jsx', 'features/hall/MenuDosModulos.jsx', 'features/gestao/ModalGestao.jsx']) {
  assert.match(fonte(caminho), /telasDoMenu\((modulo|item), \{ fonteApi, acoes \}\)/, caminho)
}
assert.match(fonte('features/hall/HallDeModulos.jsx'), /modulosDoHall\(\{ fonteApi, acoes \}\)/)
assert.match(fonte('features/hall/MolduraDoModulo.jsx'), /Seu perfil não abre esta tela/)
assert.match(fonte('features/hall/MolduraDoModulo.jsx'), /if \(primeiraViva\) window\.location\.replace\(hashDoModulo\(moduloId, primeiraViva\)\)/, 'a rota direta cai na primeira tela viva')

// As leituras que a API recusa não saem sem a ação.
assert.match(fonte('app/FaixaApi.jsx'), /useIntegracoesApi\(\{ ativo: fonteApi && podeVerIntegracoes\(acoesDoPerfil\), intervaloMs: 60_000 \}\)/)
const entregasApi = fonte('aplicacao/useEntregasApi.js')
assert.match(entregasApi, /comVitrine \? api\.listarJanelas\(\)\.catch\(\(\) => null\) : null/)
assert.match(entregasApi, /comVitrine \? api\.listarBloqueios\(data, data\)\.catch\(\(\) => null\) : null/)
const conteudo = fonte('features/entregas/ConteudoEntregasApi.jsx')
assert.match(conteudo, /const comVitrine = podeAjustarEntrega\(useAcessoModulos\(\)\.acoes\)/)
assert.match(conteudo, /\{aba === 'cadastro' && comVitrine && <CadastroEntregaApi \/>\}/)
assert.match(fonte('aplicacao/useSincronizacaoApi.js'), /if \(!comDesacertosRef\.current\) return\s+const alertas = await lerAlertasDeEstoque\(\)/)
assert.match(fonte('aplicacao/AtendimentoProvider.jsx'), /comDesacertos: podeVerDesacertos\(acoesDoPerfil\)/)
console.log('Perfis: a atendente não vê Janelas de entrega nem pede integrações, janelas e desacertos sem a ação.')

