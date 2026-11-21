// #2978: janela Novo pedido. Revisar completo (janela, endereço, pagamento, frete, com "Alterar" e sem registrar sem
// endereço válido), rascunho que devolve o endereço, aviso de celular coerente, erro único e contador de itens. Sem servidor.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
  load(url, c, next) { return url.endsWith('/infra/fonteDados.js') ? { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" } : next(url, c) },
})
const j = await import('../src/aplicacao/janelaDoPedido.js')
const { linhasDaRevisao } = await import('../src/dominio/resumoPedido.js')
const ler = (p) => readFileSync(new URL(`../src/${p}`, import.meta.url), 'utf8')
const jsx = ler('app/NovoPedidoJanela.jsx')
const css = ler('app/novoPedidoJanela.module.css')

// 1. Revisar: quatro linhas, e o que falta aparece como pendente.
{
  const pedido = { janela: 'j1|2026-11-21', meio: 'pix' }
  const l = linhasDaRevisao({ pedido, simulacao: { frete: 8 }, endereco: 'Rua A, 10', rotuloDaJanela: 'sáb · 14h' })
  assert.deepEqual(l.map((x) => x.chave), ['janela', 'endereco', 'pagamento', 'frete'])
  assert.equal(l[0].valor, 'sáb · 14h')
  assert.equal(l[1].valor, 'Rua A, 10')
  assert.equal(l[2].valor, 'Pix')
  assert.match(l[3].valor, /8,00/)
  assert.equal(l.every((x) => !x.pendente), true)
  const vazio = linhasDaRevisao({ pedido: {}, simulacao: { freteACombinar: true } })
  assert.equal(vazio.every((x) => x.pendente), true)
  assert.match(vazio[1].valor, /endereço/i)
  assert.match(vazio[3].valor, /combinar/i)
  assert.match(linhasDaRevisao({ pedido: { ...pedido, freteCombinado: 0 } })[3].valor, /combinado/)
  assert.match(jsx, /linhasDaRevisao\(/)
  assert.match(jsx, /setPasso\('entrega'\)\}>Alterar</, 'cada linha volta ao passo Entrega')
}

// 2. Sem endereço válido o Revisar não registra, e diz o motivo.
{
  const base = { passo: 'revisar', itens: [{ sku: 'a', qtd: 1 }], pedido: { janela: 'j1|x', meio: 'pix' } }
  assert.equal(j.motivoDeBloqueio({ ...base, enderecoValido: false }), 'Cadastre o endereço de entrega: o pedido usa o do cadastro, não o digitado')
  assert.equal(j.motivoDeBloqueio({ ...base, enderecoValido: true }), null)
  assert.equal(j.motivoDeBloqueio({ ...base, pedido: {} }), 'Escolha a janela de entrega')
  assert.match(jsx, /disabled=\{registrando \|\| Boolean\(motivoAvancar\)\}/)
  assert.match(jsx, /motivoDeBloqueio\(\{[^}]*enderecoValido/)
}

// 3. Rascunho: o endereço cadastrado volta junto do digitado.
{
  const r = j.rascunhoDaMontagem({ clienteId: 'c1' }, { itens: [{ sku: 'a', qtd: 1 }] }, 'revisar', [], { endereco: 'Rua A, 10', enderecoCadastrado: 'Rua A, 10' })
  assert.equal(r.enderecoCadastrado, 'Rua A, 10')
  const volta = j.restaurarRascunho(r, [{ sku: 'a', nome: 'A', preco: 1, ativo: true }])
  assert.equal(volta.endereco, 'Rua A, 10')
  assert.equal(volta.enderecoCadastrado, 'Rua A, 10')
  assert.match(jsx, /setEnderecoCadastrado\(volta\.enderecoCadastrado\)/)
  assert.match(jsx, /\{ entrega, endereco: enderecoTexto, enderecoCadastrado \}/)
}

// 4. Sem celular: caixa desmarcada e desabilitada; o texto não promete envio.
{
  assert.match(jsx, /checked=\{enviar\} disabled=\{semCelular\}/)
  assert.match(jsx, /enviarAoCliente: enviar,/, 'o Registrar usa o valor efetivo')
  assert.match(jsx, /semCelular \? '[^']*' : `Vamos enviar o resumo/)
  assert.equal(j.avisoSemCelular({ canal: 'Instagram', cliente: {} }, true), 'A cliente não será avisada: falta o celular')
}

// 5. Erro único: ao recusar, o aviso do provedor (e a barra) é fechado; fica o texto perto do Registrar.
{
  let fechou = 0
  const acoes = { gerarPedido: async () => ({ erro: 'Frete fora da área' }), fecharAvisoApi: () => { fechou += 1 } }
  const r = await j.registrarDaJanela({ acoes, janela: { conversaId: 'c1' }, meio: 'pix' })
  assert.equal(r.erro, 'Frete fora da área')
  assert.equal(fechou, 1)
  const ok = await j.registrarDaJanela({ acoes: { gerarPedido: async () => ({ pedidoId: 'p' }), fecharAvisoApi: () => { fechou += 1 } }, janela: { conversaId: 'c1' } })
  assert.equal(ok.ok, true)
  assert.equal(fechou, 1, 'sucesso não apaga o aviso')
}

// 6. Itens: contador rotulado, selo em vez de "2x" no nome, botões de 44 px com aria-label.
{
  assert.match(jsx, /role="group" aria-label=\{`Quantidade de /)
  assert.match(jsx, /aria-label=\{`Tirar um /)
  assert.match(jsx, /aria-label=\{`Somar um /)
  assert.match(jsx, /<output aria-label=\{`\$\{l\.qtd\} no pedido`\}>/)
  assert.match(jsx, /className=\{css\.selo\}/)
  assert.doesNotMatch(jsx, /\}x <\/strong>/, 'a quantidade não prefixa mais o nome')
  assert.match(css, /\.toque44\s*\{[^}]*min-width: 44px;[^}]*min-height: 44px/)
  assert.doesNotMatch(css, /border-left/, 'sem borda colorida só de um lado')
}

console.log('prova-2978-janela-pedido: ok')
