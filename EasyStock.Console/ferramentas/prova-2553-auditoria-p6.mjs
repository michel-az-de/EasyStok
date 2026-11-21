/* eslint-disable no-console */
// Prova da #2553: pacote 6 da auditoria QA de 13/11 (`docs/dev/revisoes/2026-11-13-qa-console-web.md`).
//
// Q10 Agenda: janela de hoje que já terminou aparece encerrada, a ocupação diz "ocupadas de" e o nome
//     que só repete o horário some. Q09 Relatórios: a legenda lida pelo leitor de tela diz a data por
//     extenso, não "2026-11-01". Q14 Novo prato: rótulo fora do controle, dica em aria-describedby.
//     Q17 Banco de imagens: miniaturas com loading="lazy".
//
//   node ferramentas/prova-2553-auditoria-p6.mjs

import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok  ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA  ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}
const tentar = async (caminho) => { try { return await import(caminho) } catch { return {} } }
const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')

const agenda = await tentar('../src/dominio/agendaDeEntrega.js')
const formato = await import('../src/dominio/formato.js')

// 14/11/2026 20:27 em São Paulo (UTC-3) = 23:27Z.
const AS_20H27 = Date.parse('2026-11-14T23:27:00Z')

// Q10
caso('janela de hoje que já terminou está encerrada', () => {
  assert.equal(agenda.janelaEncerrada?.({ data: '2026-11-14', fim: '12:00:00' }, AS_20H27), true)
  assert.equal(agenda.janelaEncerrada({ data: '2026-11-14', fim: '17:00' }, AS_20H27), true)
})
caso('janela de hoje ainda aberta, de amanhã ou terminando agora não está encerrada', () => {
  assert.equal(agenda.janelaEncerrada({ data: '2026-11-14', fim: '21:00:00' }, AS_20H27), false)
  assert.equal(agenda.janelaEncerrada({ data: '2026-11-15', fim: '12:00:00' }, AS_20H27), false)
  assert.equal(agenda.janelaEncerrada({ data: '2026-11-14', fim: '20:28' }, AS_20H27), false)
})
caso('o dia da loja vale, não o UTC (23:27Z ainda é dia 14 em São Paulo)', () => {
  assert.equal(agenda.janelaEncerrada({ data: '2026-11-15', fim: '09:00' }, AS_20H27), false)
})
caso('ocupação diz quantas estão ocupadas de quantas', () => {
  assert.equal(agenda.ocupacaoDaJanela?.({ ocupadas: 0, capacidade: 3 }), '0 ocupadas de 3')
  assert.equal(agenda.ocupacaoDaJanela({ ocupadas: 1, capacidade: 3 }), '1 ocupada de 3')
})
caso('nome que só repete o horário some; nome próprio continua', () => {
  assert.equal(agenda.nomeDaJanela?.('12h00 às 14h00', '12:00:00', '14:00:00'), null)
  assert.equal(agenda.nomeDaJanela('12h às 14h', '12:00:00', '14:00:00'), null)
  assert.equal(agenda.nomeDaJanela('12:00 - 14:00', '12:00:00', '14:00:00'), null)
  assert.equal(agenda.nomeDaJanela('Manhã', '09:00:00', '12:00:00'), 'Manhã')
  assert.equal(agenda.nomeDaJanela('12h às 14h', '12:00:00', '15:00:00'), '12h às 14h')
  assert.equal(agenda.nomeDaJanela('', '09:00:00', '12:00:00'), null)
})
const telaAgenda = fonte('features/entregas/AgendaEncomendasApi.jsx')
caso('a Agenda usa as três regras e não diz mais "0 de 3 vagas"', () => {
  assert.doesNotMatch(telaAgenda, /\{janela\.ocupadas\} de \{janela\.capacidade\} vagas/)
  for (const nome of ['janelaEncerrada', 'ocupacaoDaJanela', 'nomeDaJanela']) assert.match(telaAgenda, new RegExp(`${nome}\\(`))
  assert.match(telaAgenda, /Encerrada/)
})

// Q09
caso('data por extenso do dia ISO', () => {
  assert.equal(formato.dataPorExtenso?.('2026-11-01'), '1 de novembro de 2026')
  assert.equal(formato.dataPorExtenso('2026-11-30'), '30 de novembro de 2026')
})
caso('a legenda do relatório não lê a data em ISO', () => {
  const tela = fonte('features/gestao/financeiro/PainelFinanceiro.jsx')
  assert.doesNotMatch(tela, /de \{filtros\.inicio\} a \{filtros\.fim\}<\/caption>/)
  assert.match(tela, /<caption className="sr">[^<]*dataPorExtenso\(filtros\.inicio\)/)
})

// Q14
const editor = fonte('features/cardapio/EditorItemCardapio.jsx')
caso('nenhum <label> do editor do prato envolve controle', () => {
  assert.doesNotMatch(editor, /<label className=\{css\.campo\}>[^<]*<(input|select|textarea)/)
})
caso('dica fora do rótulo e ligada por aria-describedby', () => {
  assert.doesNotMatch(editor, /<label[^>]*>[^<]*<(input|select|textarea)[^]*?<small>[^<]*<\/small><\/label>/)
  const campo = editor.slice(editor.indexOf('function CampoDaFicha'))
  assert.match(campo.slice(0, 900), /htmlFor=\{id\}/)
  assert.match(campo.slice(0, 900), /aria-describedby/)
  assert.match(editor, /<CampoDaFicha rotulo="Descrição para o cliente" dica="Até 240 caracteres/)
  assert.match(editor, /<CampoDaFicha rotulo="Alérgenos" dica=/)
})

// Q17
caso('miniaturas do acervo carregam sob demanda', () => {
  for (const arquivo of ['features/gestao/acervo/AbaAcervo.jsx', 'features/gestao/acervo/SugestoesDoAcervo.jsx']) {
    const imagens = fonte(arquivo).match(/<img [^>]*>/g) ?? []
    assert.ok(imagens.length > 0, arquivo)
    for (const img of imagens) assert.match(img, /loading="lazy"/, `${arquivo}: ${img.slice(0, 60)}`)
  }
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
