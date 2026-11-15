/* eslint-disable no-console */
// Prova da #2679 (K1, homologação #1769): quando a leitura da lista de Cadastros falha, a tela mostra o erro
// e não afirma "Nenhum ... cadastrado" nem "0 cadastros". Antes, Clientes, Produtos, Fornecedores e Contas
// bancárias exibiam o alerta e, junto, o estado vazio com o botão de cadastrar e a contagem zero.
//
// Roda com: node ferramentas/prova-2679-cadastros-leitura.mjs
// Contra outra árvore (ex.: o master, para ver o Red): PROVA_SRC=<pasta src> node ferramentas/prova-2679-cadastros-leitura.mjs
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { pathToFileURL } from 'node:url'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})
const base = process.env.PROVA_SRC ? pathToFileURL(process.env.PROVA_SRC.replace(/[\\/]?$/, '/')) : new URL('../src/', import.meta.url)
const dominio = await import(new URL('dominio/cadastros.js', base).href)
const painel = readFileSync(new URL('features/gestao/cadastros/PainelCadastros.jsx', base), 'utf8')

let falhas = 0
const caso = (nome, fn) => { try { fn(); console.log(`ok   ${nome}`) } catch (e) { falhas++; console.log(`FALHA ${nome}\n     ${e.message}`) } }

caso('K1 domínio: leitura falha é estado próprio, não lista vazia', () => {
  assert.equal(typeof dominio.leituraDaLista, 'function', 'dominio/cadastros.js exporta leituraDaLista')
  assert.equal(dominio.leituraDaLista({ carregando: true, erro: null }), 'carregando')
  assert.equal(dominio.leituraDaLista({ carregando: false, erro: 'Sem conexão com o EasyStok.' }), 'falhou')
  assert.equal(dominio.leituraDaLista({ carregando: false, erro: null }), 'pronta')
  // A aba "Sem cadastro" lista conversas já carregadas, não a API de cadastros.
  assert.equal(dominio.leituraDaLista({ carregando: false, erro: 'x' }, true), 'pronta')
})

caso('K1 tela: com a leitura falha não há estado vazio', () => {
  const corpo = painel.slice(painel.indexOf("titulo=\"Carregando cadastros\""))
  const falhou = corpo.indexOf("leitura === 'falhou'")
  const vazio = corpo.indexOf('lista.length === 0')
  assert.ok(falhou >= 0 && vazio > falhou, 'o ramo da falha vem antes do estado vazio')
})

caso('K1 tela: com a leitura falha o rodapé não conta zero nem pagina', () => {
  const rodape = painel.slice(painel.indexOf('<footer className={css.rodape}>'))
  assert.match(rodape, /<span>\{leitura === 'falhou' \? 'Lista não carregada'/, 'rodapé sem contagem')
  assert.match(rodape, /\{leitura !== 'falhou' && !soLeads && !consulta\.pesquisaLimitada/, 'paginação só com a lista lida')
})

console.log(falhas ? `\n${falhas} falha(s)` : '\nprova-2679 ok')
process.exit(falhas ? 1 : 0)
