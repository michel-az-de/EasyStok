/* eslint-disable no-console */
// Prova da #1551 (banca de leitura do console, delta de 10/10). São três ligações de tela, sem
// regra de domínio nova, então a prova lê o JSX e exige a ligação no lugar certo:
//   1. tablet: a Ficha abre em <dialog> e o aviso da ação da API (janela faltando, pedido não
//      criado, cobrança que não saiu) precisa estar DENTRO dela; a FaixaApi fica atrás da cortina;
//   2. o seletor de janela do rascunho remonta por conversa: o "Dia da entrega" escolhido para um
//      cliente não fica valendo para o seguinte;
//   3. "Modelos aprovados" só aparece com a API ligada: na demonstração o botão só devolvia erro.
// Cada catraca foi provada por mutação (desfazer a correção reprova a linha correspondente).
//
//   node ferramentas/prova-1551-delta-console.mjs

import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

// No Windows o checkout vem com CRLF; a prova lê como o CI (LF).
const ler = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8').replace(/\r\n/g, '\n')
// Comentário não conta como ligação: sai antes de procurar.
const semComentarios = (fonte) => fonte
  .replace(/\{\/\*[\s\S]*?\*\/\}/g, '')
  .replace(/\/\*[\s\S]*?\*\//g, '')
  .replace(/^\s*\/\/.*$/gm, '')
const vezes = (fonte, trecho) => fonte.split(trecho).length - 1
// Corpo de uma função declarada no topo do arquivo, até a chave que fecha na coluna zero.
function corpoDe(fonte, nome) {
  const inicio = fonte.indexOf(`function ${nome}(`)
  assert.notEqual(inicio, -1, `função ${nome} não encontrada`)
  const fim = fonte.indexOf('\n}\n', inicio)
  assert.notEqual(fim, -1, `fim da função ${nome} não encontrado`)
  return fonte.slice(inicio, fim)
}
// A tag de abertura de um componente, com todas as props.
function tagDe(fonte, nome) {
  const inicio = fonte.indexOf(`<${nome}\n`) !== -1 ? fonte.indexOf(`<${nome}\n`) : fonte.indexOf(`<${nome} `)
  assert.notEqual(inicio, -1, `<${nome}> não encontrado`)
  const fim = fonte.indexOf('/>', inicio)
  assert.notEqual(fim, -1, `fim de <${nome}> não encontrado`)
  return fonte.slice(inicio, fim)
}

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try {
    fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

// --- 1. Aviso da ação dentro da gaveta da Ficha -------------------------------------------
const moldura = semComentarios(ler('app/Moldura.jsx'))
confere('a gaveta da Ficha (tablet) leva o aviso da ação da API para dentro do dialog', () => {
  assert.ok(corpoDe(moldura, 'Gaveta').includes('<AvisoDaAcaoApi />'), 'Gaveta sem <AvisoDaAcaoApi />')
  assert.match(moldura, /import \{[^}]*AvisoDaAcaoApi[^}]*\} from '\.\/FaixaApi'/)
})
confere('o aviso entra uma vez só e a gaveta só existe no arranjo de tablet (desktop não duplica)', () => {
  assert.equal(vezes(moldura, '<AvisoDaAcaoApi'), 1)
  assert.equal(vezes(moldura, '<Gaveta '), 1)
  const tablet = moldura.indexOf('if (tamanho === TABLET)')
  const celular = moldura.indexOf('const painelDaAba')
  const gaveta = moldura.indexOf('<Gaveta ')
  assert.ok(tablet !== -1 && celular > tablet, 'arranjos de tablet e celular não encontrados')
  assert.ok(gaveta > tablet && gaveta < celular, '<Gaveta> fora do arranjo de tablet')
})
confere('sem aviso, o rodapé vazio da gaveta não ocupa espaço', () => {
  const css = ler('app/moldura.module.css')
  assert.match(css, /\.gaveta > h2 \+ div \+ div:empty \{ display: none; \}/)
})
confere('o aviso continua calado na demonstração e sem aviso pendente', () => {
  const faixa = semComentarios(ler('app/FaixaApi.jsx'))
  assert.ok(corpoDe(faixa, 'AvisoDaAcaoApi').includes('if (!fonteApi || !sincronizacao?.aviso) return null'))
})

// --- 2. Dia da entrega por conversa -------------------------------------------------------
const blocoPedido = semComentarios(ler('features/ficha-cliente/BlocoPedido.jsx'))
const painelFicha = semComentarios(ler('features/ficha-cliente/PainelFicha.jsx'))
confere('o seletor de janela da API remonta quando a conversa muda', () => {
  assert.ok(tagDe(blocoPedido, 'SeletorJanelaApi').includes('key={conversaId}'), '<SeletorJanelaApi> sem key por conversa')
  assert.match(corpoDe(blocoPedido, 'BlocoPedido'), /nomeCliente, endereco, faixa, bloqueado = false, conversaId,/)
})
confere('a Ficha entrega o id da conversa à comanda', () => {
  assert.ok(tagDe(painelFicha, 'BlocoPedido').includes('conversaId={selecionada.id}'), '<BlocoPedido> sem conversaId')
})
confere('o dia escolhido é estado local reiniciado com a data da comanda, vazia por padrão', () => {
  const seletor = semComentarios(ler('features/ficha-cliente/SeletorJanelaApi.jsx'))
  assert.ok(corpoDe(seletor, 'SeletorJanelaApi').includes("dataInicial = ''"))
  assert.ok(tagDe(seletor, 'EscolhaRascunho').includes('dataInicial={dataInicial}'))
  assert.ok(corpoDe(seletor, 'EscolhaRascunho').includes('const [data, setData] = useState(dataInicial)'))
})

// --- 3. Modelos aprovados só com a API ----------------------------------------------------
const painelAtendimento = semComentarios(ler('features/atendimento/PainelAtendimento.jsx'))
const composer = semComentarios(ler('features/atendimento/Composer.jsx'))
confere('"Modelos aprovados" exige a fonte API', () => {
  assert.ok(
    tagDe(painelAtendimento, 'Composer').includes('podeEnviarModelo={fonteApi && podeReabrir && canal.aceitaModelo && !encerrada && !bloqueada}'),
    'podeEnviarModelo sem exigir fonteApi',
  )
})
confere('botão e dica do campo dependem do mesmo sinal: sem o botão, a dica não o promete', () => {
  assert.match(composer, /\{podeEnviarModelo && <Botao className=\{css\.abrirModelos\}/)
  assert.match(composer, /podeEnviarModelo \? 'Use Modelos aprovados para continuar' : 'Mensagem indisponível nesta conversa'/)
  assert.equal(vezes(composer, 'Modelos aprovados'), 2)
})
confere('na demonstração a ação continua recusando, sem fingir envio', () => {
  const acoes = ler('aplicacao/criarAcoes.js')
  assert.equal(vezes(acoes, "throw new Error('Modelos aprovados exigem a conexão real com o EasyStok.')"), 2)
})

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length) process.exit(1)
