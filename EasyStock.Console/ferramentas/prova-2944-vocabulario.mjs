/* eslint-disable no-console */
// Prova da #2944 (E1-12): o vocabulário novo está nas telas e os rótulos velhos saíram.
//   node ferramentas/prova-2944-vocabulario.mjs
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')

const casos = [
  ['features/ficha-cliente/BlocoEntrega.jsx', /rotulo: 'Saiu para entrega'/, /rotulo: 'Saiu',/],
  ['app/NovoPedidoJanela.jsx', /'Registrar pedido'/, /: 'Registrar'\}/],
  ['features/ficha-cliente/BarraProximoPasso.jsx', /Entregar e cobrar depois/, /Marcar entregue mesmo assim/],
  ['features/caixa-de-entrada/NaoEntregues.jsx', /titulo="Mensagens não entregues"/, /titulo="Não entregues"/],
  ['features/gestao/cadastros/CelularDoPedido.jsx', /rotulo="Celular com DDD \(para enviar o resumo\)"/, /rotulo="Celular com DDD"/],
]
for (const [arquivo, novo, velho] of casos) {
  const texto = fonte(arquivo)
  assert.match(texto, novo, `${arquivo} diz o texto novo`)
  assert.doesNotMatch(texto, velho, `${arquivo} não diz o texto velho`)
}

// Item 1: cobrança em um rótulo só, vindo do glossário.
const { ROTULOS } = await import('../src/dominio/rotulos.js')
assert.equal(ROTULOS.GERAR_COBRANCA, 'Gerar cobrança')
for (const arquivo of ['features/ficha-cliente/BarraProximoPasso.jsx', 'features/ficha-cliente/BlocoCobranca.jsx', 'dominio/resumoPedido.js']) {
  const texto = fonte(arquivo)
  assert.match(texto, /ROTULOS\.GERAR_COBRANCA/, `${arquivo} usa o rótulo do glossário`)
  assert.doesNotMatch(texto.replace(/^\s*\/\/.*$/gm, ''), /['>]\s*Gerar cobrança/, `${arquivo} não repete o literal`)
}
const { ROTULO_GERAR_E_ENVIAR } = await import('../src/dominio/resumoPedido.js')
assert.equal(ROTULO_GERAR_E_ENVIAR, 'Gerar cobrança e enviar')

// Item 2: "modelo aprovado" sai da exibição e o termo vem do glossário.
for (const arquivo of ['features/atendimento/ModalEnviarModelo.jsx', 'features/gestao/atendimento/AbaAtendimento.jsx', 'features/atendimento/ModalProgramarMensagem.jsx', 'dominio/mensagemProgramada.js']) {
  const texto = fonte(arquivo).replace(/^\s*\/\/.*$/gm, '')
  assert.doesNotMatch(texto, /modelos? aprovados?/i, `${arquivo} não diz "modelo aprovado"`)
  assert.match(texto, /ROTULOS\.MENSAGEM_PADRAO/, `${arquivo} usa o glossário`)
}
const { faltaParaProgramar } = await import('../src/dominio/mensagemProgramada.js')
assert.match(faltaParaProgramar({ agendadaPara: '2999-01-01T10:00', agoraMs: 0, usarModelo: true, modeloPronto: false }), /mensagem padrão do WhatsApp/)

// Item 3: o campo do celular da cliente diz "Celular (com DDD)".
assert.equal(ROTULOS.CELULAR, 'Celular (com DDD)')
for (const arquivo of ['features/ficha-cliente/BlocoCliente.jsx', 'features/ficha-cliente/BlocoCapturaAutomatica.jsx', 'features/ficha-cliente/ContatoInformado.jsx']) {
  const texto = fonte(arquivo)
  assert.match(texto, /ROTULOS\.CELULAR/, `${arquivo} usa Celular (com DDD)`)
  assert.doesNotMatch(texto, /rotulo="Telefone"|rotulo: 'Telefone'|<dt>Telefone<\/dt>|>Telefone </, `${arquivo} não diz Telefone no campo`)
}

// Item 4: visitante com número é "Pedido N"; cartão sem nome ganha o selo.
const { apresentacaoDoNome } = await import('../src/dominio/nomeDoContato.js')
assert.equal(apresentacaoDoNome('Comanda 21').titulo, 'Visitante do site · Pedido 21')
assert.equal(apresentacaoDoNome('Comanda 21').capsula.texto, '#21')
assert.match(fonte('features/ficha-cliente/CartaoDoCliente.jsx'), /Pedido de \$\{/)
assert.match(fonte('features/ficha-cliente/CartaoDoCliente.jsx'), /clienteSemNome|semNomeDeVerdade|SEM_NOME/)

// Item 5: "Comanda" vira "Pedido" no texto exibido (telas inteiras); a lógica segue com a chave.
const telas = ['features/cardapio/PainelCardapio.jsx', 'features/ficha-cliente/ComandaDoCliente.jsx', 'features/ficha-cliente/BlocoPedido.jsx', 'features/ficha-cliente/ConferenciaPessoaDaComanda.jsx', 'features/atendimento/ModalEnviarCardapio.jsx', 'features/ficha-cliente/Cabecalho.jsx']
const TEXTO = new RegExp(["'[^'\\n]*'",'"[^"\n]*"', '`[^`\n]*`', '>[^<>{}\n]+<'].join('|'), 'g')
for (const arquivo of telas) {
  const exibido = [...fonte(arquivo).replace(/\{\/\*[\s\S]*?\*\/\}/g, '').replace(/^\s*(\/\/|\*|\/\*).*$/gm, '').matchAll(TEXTO)].map((m) => m[0])
  const velho = exibido.filter((t) => /(?<![\w-])[Cc]omanda(?![\w:-])/.test(t) && !/id=|comanda-|data-|.css|comanda:/.test(t) && !/^['"`]comanda['"`]$/.test(t))
  assert.deepEqual(velho, [], `${arquivo} ainda diz "comanda" na tela`)
}
assert.equal(apresentacaoDoNome('Comanda 6').capsula.texto, '#6', 'ehVisitante segue casando "Comanda 6"')

// Item 6 (E1, telas que ficaram de fora): Ficha, frases do resumo, Cozinha e cartão da lista dizem "Pedido".
// Impressão (`impressao.js`, `ComandaHtml.cs`) fica de fora por regra da spec (linha "NÃO trocar").
const semComentario = (arquivo) => fonte(arquivo).replace(/^\s*\/\/.*$/gm, '')
const ficha = semComentario('features/ficha-cliente/PainelFicha.jsx')
assert.doesNotMatch(ficha, /'Revisar comanda'|`Comanda \$\{numero\}`|: 'Comanda'\}|>Nova comanda<|`Comanda · /, 'PainelFicha diz Pedido')
assert.match(ficha, /Revisar pedido/)
const resumo = await import('../src/dominio/resumoPedido.js')
const frasesDoResumo = [
  resumo.situacaoDoEnvio({ itens: [], estado: 'aguardando' }).texto,
  resumo.avisoDoEnvio({ link: 'x', meio: 'pix' }, 'Ana'),
  resumo.avisoDoEnvio({ meio: 'dinheiro' }, 'Ana'),
  resumo.caminhoParaTrocarItem({ estado: 'aguardando' }),
]
for (const frase of frasesDoResumo) assert.doesNotMatch(frase, /comanda/i, `resumoPedido: "${frase}"`)
const { nomeDoPedido } = await import('../src/dominio/pedido.js')
assert.equal(nomeDoPedido({ numeroComanda: 12 }).frase, 'Pedido 12')
assert.equal(nomeDoPedido({ numeroComanda: 12 }).principal, 'Pedido 12')
assert.doesNotMatch(semComentario('features/cozinha/CartaoKds.jsx'), /Ver comanda/)
const { nomeDoCartao } = await import('../src/features/caixa-de-entrada/apresentacaoDoCartao.js')
const cartaoNomeado = nomeDoCartao({ nome: 'Ana Souza', numeroComanda: 16 })
assert.equal(cartaoNomeado.complemento, 'Pedido 16')
assert.equal(cartaoNomeado.numeroPedido, 16, 'o número vai como dado, sem regex de texto')
assert.doesNotMatch(semComentario('features/caixa-de-entrada/CartaoConversa.jsx'), /\.replace\(\/\^Comanda/)
assert.doesNotMatch(semComentario('features/atendimento/PainelAtendimento.jsx'), />Comanda \{|<>Comanda /)

console.log('prova-2944-vocabulario: ok')
