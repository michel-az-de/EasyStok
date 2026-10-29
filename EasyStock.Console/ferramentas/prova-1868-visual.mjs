/* eslint-disable no-console */
// Prova da issue #1868 (polimento visual do console no desktop, grupo B: condições no JSX).
// Medido antes no laboratório, em 1280/1440/1920: "Visitante do site" era o nome em 37 de 39
// cartões; "Aguardando equipe" e "Chat do site" se repetiam em 38 de 39; o saldo da ficha estava a
// 15 px cinza e aparecia 3 vezes na coluna; a modal Pagamento dizia "R$ 32,50" 5 vezes; a janela
// aparecia 2 vezes na modal Pedido e entrega; o composer do chat do site tinha 4 ícones apagados.
//
//   1  cartão do Balcão: nome exibido, origem e etiqueta do estado;
//   2  Resumo: saldo em destaque uma vez, sem repetição no acesso Pagamento;
//   3  modais: Cobrança sem o Total do Extrato; papel sem a janela da linha de baixo;
//   4  conversa: o composer mostra só o que o canal entrega; o pedido do site se lê no fio;
//   5  a informação obrigatória continua na tela (saldo, janela, motivo do desabilitado).
//
// Regras puras e leitura do fonte onde só existe composição. As medidas de antes e depois
// na tela real saem dos roteiros de captura da banca visual.
//
//   node ferramentas/prova-1868-visual.mjs

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

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
const cartao = await import('../src/features/caixa-de-entrada/apresentacaoDoCartao.js')
const ficha = await import('../src/features/ficha-cliente/apresentacaoDaFicha.js')
const conversa = await import('../src/features/atendimento/apresentacaoDaConversa.js')
const { moeda } = await import('../src/dominio/formato.js')

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log('ok  ' + descricao) }
  catch (erro) { falhas.push(descricao); console.error('FALHOU  ' + descricao + '\n        ' + String(erro.message).split('\n')[0]) }
}

// --- 1. Cartão do Balcão -----------------------------------------------------------------------

confere('1  visitante genérico com comanda: a comanda vira o nome e o nome inteiro fica no title', () => {
  const nome = cartao.nomeDoCartao({ nome: 'Visitante do site', numeroComanda: 16 })
  assert.deepEqual(nome, { principal: 'Comanda 16', complemento: null, titulo: 'Visitante do site · Comanda 16' })
})
confere('1  cliente com nome de verdade mantém o nome e a comanda ao lado', () => {
  assert.deepEqual(cartao.nomeDoCartao({ nome: 'Felipel Azevedo', numeroComanda: 3 }),
    { principal: 'Felipel Azevedo', complemento: 'Comanda 3', titulo: 'Felipel Azevedo · Comanda 3' })
  assert.deepEqual(cartao.nomeDoCartao({ nome: 'Ana Souza' }), { principal: 'Ana Souza', complemento: null, titulo: 'Ana Souza' })
})
confere('1  visitante sem comanda continua "Visitante do site" (não inventa número)', () => {
  assert.equal(cartao.nomeDoCartao({ nome: 'Visitante do site', numeroComanda: null }).principal, 'Visitante do site')
})
confere('1  "Chat do site" sai da origem (o selo do avatar já diz); as outras origens dizem o porquê', () => {
  assert.equal(cartao.origemNoCartao({ origem: 'chat-site', motivo: 'Visitante do site aguardando atendimento da equipe.' }), null)
  assert.equal(cartao.origemNoCartao(null), null)
  const reabertura = cartao.origemNoCartao({ origem: 'reabertura', motivo: 'Cliente voltou a escrever' })
  assert.equal(reabertura.curto, 'Cliente voltou a escrever')
  const naoSoube = cartao.origemNoCartao({ motivo: 'pediu desconto', em: '2026-10-16T14:00:00Z' })
  assert.match(naoSoube.curto, /^Automático não soube responder/)
})
// #1899: a função recebe o estado da linha (que herda a chave do motivo quando há motivo).
confere('1  etiqueta do estado some na passagem, no Automático e no Com você; motivos, Bloqueado, Encerrada e Outro ficam', () => {
  for (const chave of ['passagem', 'automatico', 'voce']) assert.equal(cartao.mostraEtiquetaDoEstado({ chave }), false, chave)
  for (const chave of ['reclamacao', 'cobranca', 'esperando', 'pedido-atrasado', 'atraso-entrega', 'aguardando-abertura', 'bloqueado', 'encerrada', 'outro']) {
    assert.equal(cartao.mostraEtiquetaDoEstado({ chave }), true, chave)
  }
  assert.equal(cartao.mostraEtiquetaDoEstado(null), true, 'sem estado conhecido, não esconde')
})
confere('1  a terceira linha só existe com etiqueta ou contexto', () => {
  assert.equal(cartao.temLinhaDeContexto({ etiqueta: false, contexto: [] }), false)
  assert.equal(cartao.temLinhaDeContexto({ etiqueta: false, contexto: [{ chave: 'pedido', texto: 'Sinal pago' }] }), true)
  assert.equal(cartao.temLinhaDeContexto({ etiqueta: true, contexto: [] }), true)
})
confere('1  sem a etiqueta, o estado vai para o leitor de tela e a atenção vira trilho lateral', () => {
  const jsx = fonte('features/caixa-de-entrada/CartaoConversa.jsx')
  assert.match(jsx, /\{!etiqueta && <span className="sr">, \{estado\.texto\}<\/span>\}/)
  assert.match(jsx, /title=\{nome\.titulo\}/)
  assert.match(fonte('features/caixa-de-entrada/caixa.module.css'), /\.cartao\.precisa, \.cartao\.precisa\[aria-current='true'\] \{ box-shadow: inset 3px 0 0 var\(--alerta\); \}/)
  assert.match(fonte('features/caixa-de-entrada/classesDoCartao.js'), /motivo && motivo\.tom !== 'perigo' \? css\.precisa/) // #2135: todo motivo tem trilho, não só o `aviso`
})

// --- 2. Resumo ---------------------------------------------------------------------------------

confere('2  saldo em destaque só com pedido criado, aberto, não cancelado e com saldo', () => {
  assert.equal(ficha.saldoEmDestaque({ editavel: true, pedidoCriado: true, saldo: 32.5 }), true)
  assert.equal(ficha.saldoEmDestaque({ editavel: true, pedidoCriado: true, saldo: 0 }), false)
  assert.equal(ficha.saldoEmDestaque({ editavel: true, pedidoCriado: false, saldo: 32.5 }), false, 'seleção não é cobrança')
  assert.equal(ficha.saldoEmDestaque({ editavel: false, pedidoCriado: true, saldo: 32.5 }), false, 'encerrado não mostra valor')
  assert.equal(ficha.saldoEmDestaque({ editavel: true, pedidoCriado: true, saldo: 32.5, cancelado: true }), false)
})
confere('2  com o saldo em destaque, o acesso Pagamento não repete o valor; sem destaque, repete', () => {
  assert.equal(ficha.resumoDoPagamento({ saldo: 32.5, destaque: true }), 'Receber o saldo e ver o extrato')
  assert.equal(ficha.resumoDoPagamento({ saldo: 32.5, destaque: false }), `${moeda(32.5)} a receber`)
  assert.equal(ficha.resumoDoPagamento({ saldo: 0 }), 'Pagamento completo')
  assert.equal(ficha.resumoDoPagamento({ cancelado: true, saldo: 10 }), 'Confira recebimentos e devoluções')
})
confere('2  o saldo aparece no Resumo com o peso do valor (24 px, negrito, tinta cheia)', () => {
  assert.match(fonte('features/ficha-cliente/PainelFicha.jsx'), /\{destacaSaldo && <p className=\{css\.saldoFicha\}>\{moeda\(saldo\)\} a receber<\/p>\}/)
  const regra = fonte('features/ficha-cliente/ficha.module.css').match(/\.resumoAtual \.saldoFicha \{[^}]+\}/)?.[0] ?? ''
  assert.match(regra, /font-size: var\(--t-valor\)/)
  assert.match(regra, /font-weight: 700/)
  assert.match(regra, /color: var\(--tinta\)/)
})
confere('2  "Já coberto/Falta" só sai quando o "Marcar pago (complemento)" já diz o saldo', () => {
  const barra = fonte('features/ficha-cliente/BarraProximoPasso.jsx')
  assert.match(barra, /saldoNoBotao = complementoEmAberto\s+const botaoComplemento = complementoEmAberto && \(/)
  assert.match(barra, /\{!saldoNoBotao && alocadoApi > 0 && saldoApi\(pedido\) > 0 && \(/)
  assert.match(barra, /Marcar pago \(complemento \{moeda\(pedido\.cobranca\.valor\)\}\)/)
  // O caso sem botão (pedido na fila sem quitação) é conferido na tela por prova-premium-comanda.
})

// --- 3. Modais ---------------------------------------------------------------------------------

confere('3  Cobrança repete Total e saldo só quando o Extrato não está acima', () => {
  assert.equal(ficha.cobrancaRepeteExtrato({ extratoAcima: true }), false)
  assert.equal(ficha.cobrancaRepeteExtrato({ extratoAcima: false }), true)
  const jsx = fonte('features/ficha-cliente/BlocoCobranca.jsx')
  assert.match(jsx, /cobrancaRepeteExtrato\(\{ extratoAcima: fonteApi && extratoDoPedido\(pedido\) != null \}\)/)
  // O valor a cobrar continua na linha da maquininha, fora da condição.
  assert.match(jsx, /\{sinalPendente \? 'Sinal a receber' : 'Receber na entrega'\} · \{nome\} <b>\{moeda\(faltaSinal\)\}<\/b>/)
  assert.match(fonte('features/ficha-cliente/PainelFicha.jsx'), /\{fonteApi && <ExtratoDoPedido pedido=\{pedido\}( aDevolver=\{aDevolver\})? \/>\}\s*<BlocoCobranca/)
})
confere('3  papel da comanda sem a janela só quando a linha "Janela de entrega" está logo abaixo', () => {
  assert.equal(ficha.janelaNoCabecalhoDoPapel({ fonteApi: true, pedidoCriado: true, fechado: false }), false)
  assert.equal(ficha.janelaNoCabecalhoDoPapel({ fonteApi: true, pedidoCriado: false }), true, 'rascunho: o papel avisa a janela')
  assert.equal(ficha.janelaNoCabecalhoDoPapel({ fonteApi: true, pedidoCriado: true, fechado: true }), true, 'fechado: sem seletor abaixo')
  assert.equal(ficha.janelaNoCabecalhoDoPapel({ fonteApi: false, pedidoCriado: true }), true, 'demonstração: sem seletor da API')
  // O "Sem janela" continua no cabeçalho, independente da faixa.
  assert.match(fonte('features/ficha-cliente/BlocoPedido.jsx'), /semJanela=\{!fechado && !pedido\.janela\}/)
})

// --- 4. Conversa -------------------------------------------------------------------------------

confere('4  canal sem mídia: sem Anexar, Fotos e microfone; a barra fica enxuta', () => {
  assert.deepEqual(conversa.acoesDoComposer({ podeAnexar: false, podeFoto: false, podeAudio: false }),
    { anexar: false, fotos: false, audio: false, enxuta: true })
  assert.deepEqual(conversa.acoesDoComposer({ podeAnexar: true, podeFoto: true, podeAudio: true }),
    { anexar: true, fotos: true, audio: true, enxuta: false })
  assert.equal(conversa.acoesDoComposer({ podeAnexar: true, podeFoto: false, podeAudio: false }).enxuta, false)
})
confere('4  sem áudio o lugar do microfone fica com o Enviar, desligado até haver texto', () => {
  const jsx = fonte('features/atendimento/Composer.jsx')
  assert.match(jsx, /\{temTexto \|\| anexo \|\| !acoes\.audio \? \(/)
  assert.match(jsx, /disabled=\{!podeEscrever \|\| assumindo \|\| Boolean\(variavelSobrando\) \|\| !\(temTexto \|\| anexo\)\}/)
})
confere('4  o pedido do site no fio se lê como documento; aviso curto segue miúdo', () => {
  assert.equal(conversa.avisoDePedido('Pedido do site #13EF98F7: 1 × lasanha. Total R$ 65,00.'), true)
  assert.equal(conversa.avisoDePedido('Conversa reaberta'), false)
  assert.equal(conversa.avisoDePedido('x'.repeat(141)), true)
  assert.equal(conversa.avisoDePedido(null), false)
  const regra = fonte('features/atendimento/atendimento.module.css').match(/\.sistema\.sistemaPedido \{[^}]+\}/)?.[0] ?? ''
  assert.match(regra, /text-align: left/)
  assert.match(regra, /font-size: var\(--t-apoio\)/)
})

// --- 5. Informação obrigatória -----------------------------------------------------------------

confere('5  o motivo da restrição do canal continua escrito embaixo do campo', () => {
  // #1870: canal só de texto mostra "Só texto neste canal"; a frase inteira segue no title e no leitor de tela.
  const composer = fonte('features/atendimento/Composer.jsx')
  assert.match(composer, /\? <p id=\{idRestricao\} className=\{css\.restricao\}>\{restricao\}<\/p>/)
  assert.match(composer, /: <p id=\{idRestricao\} className=\{css\.restricao\} title=\{restricao\}>.*<span className="sr">\{restricao\}<\/span><\/p>/)
})
confere('5  a janela continua no acesso "Pedido e entrega" do Resumo e o aviso da janela de 24 h no composer', () => {
  const painel = fonte('features/ficha-cliente/PainelFicha.jsx')
  assert.match(painel, /resumo=\{editavel \? faixa \|\| 'Itens, horário e acompanhamento'/)
  assert.match(fonte('features/atendimento/Composer.jsx'), /fixo acima do campo só perto do fim da janela de 24 h/)
})
confere('5  Balcão e Resumo sem cabeçalho só no desktop de três colunas; o nome segue no aria-label', () => {
  const moldura = fonte('app/Moldura.jsx')
  assert.match(moldura, /<Coluna variante="entrada" titulo="Conversas" semTitulo>/)
  assert.match(moldura, /<Coluna variante="ficha" titulo="Cliente" semTitulo>/)
  assert.match(moldura, /<section className=\{`\$\{css\.coluna\} \$\{css\[variante\]\}`\} aria-label=\{titulo\}>/)
  assert.equal((moldura.match(/titulo="Conversas" semTitulo/g) ?? []).length, 1, 'tablet e celular mantêm o título')
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
