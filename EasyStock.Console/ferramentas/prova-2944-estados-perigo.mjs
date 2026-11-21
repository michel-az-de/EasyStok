// #2944 E1-05 (vazio, Esqueleto, "Calculando frete…") e E1-06 (descartar com perigo, "Tirar um {nome}", erro no campo).
//   node ferramentas/prova-2944-estados-perigo.mjs
import { assert, caso, fim, fonte } from './prova-2944-base.mjs'

const jsx = fonte('app/NovoPedidoJanela.jsx')

await caso('05a. busca sem resultado diz o que buscou', () => assert.match(jsx, /Nada no cardápio com/))
await caso('05b. cardápio carregando mostra EsqueletoDeLista', () => {
  assert.match(jsx, /import \{[^}]*EsqueletoDeLista[^}]*\} from '\.\.\/componentes\/Esqueleto'/)
  assert.match(jsx, /<EsqueletoDeLista\b/)
})
await caso('05c. "Calculando frete…" é status e o total fica aria-busy', () => {
  assert.match(jsx, /<p[^>]*role="status"[^>]*>Calculando frete…/)
  assert.match(jsx, /aria-busy=\{calculando/)
})
await caso('06a. Confirmar descarte é perigo e ambos travam durante o registro', () => {
  assert.match(jsx, /<Botao variante="perigo"[^\n]*disabled=\{registrando\}[^\n]*>Confirmar descarte/)
  assert.match(jsx, /<Botao[^\n]*disabled=\{registrando\}[^\n]*>Manter rascunho/)
})
await caso('06b. o botão de tirar nomeia o item', () => assert.match(jsx, /aria-label=\{`Tirar um \$\{/))
await caso('06c. erro do endereço vai no campo (CampoTexto erro=)', () => {
  assert.match(jsx, /<CampoTexto[^\n]*erro=\{erroEndereco\}/)
  assert.doesNotMatch(jsx, /\{erroEndereco && <p/)
})
fim()
