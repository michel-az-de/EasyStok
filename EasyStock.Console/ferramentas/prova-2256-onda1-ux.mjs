// Prova da #2256: ajustes visuais da Onda 1 da auditoria de UX de 05/11.
//
// Cobre a função pura das colunas da grade do Início e as regras de CSS e JSX que
// a auditoria apontou: cartões de número na altura do próprio conteúdo, contadores
// ancorados no canto do ícone do trilho e "Fechar o caixa" de outro dia como ação primária.
// O desenho em si é conferido no navegador.
//
// Roda com: node ferramentas/prova-2256-onda1-ux.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const ler = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
const regra = (texto, seletor) => {
  const inicio = texto.indexOf(`${seletor} {`)
  assert.notEqual(inicio, -1, `regra ${seletor} não encontrada`)
  return texto.slice(inicio, texto.indexOf('}', inicio))
}
let ok = 0
const caso = (nome, fn) => { fn(); ok++; console.log(`ok  ${nome}`) }

const { colunasDaGrade } = await import('../src/dominio/estadoDoHall.js')

caso('grade: 3 ou 4 colunas, a que deixa menos vagas vazias', () => {
  // 6 módulos em 4 colunas deixavam 2 vagas na segunda linha.
  assert.deepEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12].map(colunasDaGrade), [4, 4, 3, 4, 3, 3, 4, 4, 3, 4, 4])
})

caso('grade: o Início marca as colunas e o CSS só as aplica no desktop', () => {
  assert.match(ler('features/hall/HallDeModulos.jsx'), /data-colunas=\{colunasDaGrade\(modulos\.length\)\}/)
  const hall = ler('features/hall/hall.module.css')
  assert.match(hall, /@media \(min-width: 1280px\) \{\s*\.grade\[data-colunas='3'\] \{ grid-template-columns: repeat\(3, minmax\(0, 1fr\)\); \}/)
})

caso('cartões de número: mesma altura na mesma linha, sem esticar até o maior da grade', () => {
  const hall = ler('features/hall/hall.module.css')
  assert.doesNotMatch(regra(hall, '.indicador'), /height:\s*100%/)
  // #2258 (auditoria H5): a #2256 usava align-items:start (cada cartão na própria altura) e deixava cartões
  // da mesma linha com alturas diferentes. Agora a linha estica (stretch) e o cartão preenche o item (flex:1),
  // sem altura fixa: cada linha pesa só o próprio conteúdo.
  assert.match(regra(hall, '.indicadores'), /align-items:\s*stretch/)
})

caso('contadores de Avisos e Lembretes ancorados no canto do ícone do trilho', () => {
  // #2185 (R04): o contador é a Pilula contagem; o número vai no nome acessível do botão.
  assert.match(ler('features/notificacoes/SinoDaSessao.jsx'), /<Pilula contagem=\{[^}]*total[^}]*\} tom="perigo" aria-hidden="true" data-contador-sino \/>/)
  assert.match(ler('features/lembretes/Sininho.jsx'), /className=\{css\.badge\} data-contador-sino aria-hidden="true" \/>/)
  const trilho = ler('componentes/Trilho.module.css')
  assert.match(regra(trilho, '.filhos [data-contador-sino]'), /position:\s*absolute;[^}]*left:\s*calc\(50% \+ 6px\);[^}]*right:\s*auto/)
  assert.match(regra(trilho, '.filhos [data-aparelho-desligado]'), /position:\s*absolute/)
})

caso('fechar o caixa de outro dia é a ação primária do Financeiro', () => {
  const aba = ler('features/gestao/caixa/AbaCaixaApi.jsx')
  assert.match(aba, /<Botao variante=\{dia\.esquecidoAberto \? 'primario' : 'texto'\} icone="lock"/)
})

console.log(`\n${ok} casos ok`)
