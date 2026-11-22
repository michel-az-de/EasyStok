import { disponivelHoje, itemPorSku } from '../dominio/cardapio'
import { telefoneNaTela } from '../dominio/cadastros'
import { erroDoCelular } from '../dominio/celularDoPedido'
import { identidadeDaEdicao } from '../dominio/identidadeDaEdicao'
import { pedidoEncerrado } from '../dominio/pedido'
import { temRascunhoDoPedido } from '../infra/api/rascunhosDoPedido'
import { AVISO_LOJA_DESLIGADA } from './planoDeSincronizacao'

// #2921: regras puras da janela "Novo pedido" na tela. A montagem mora em `conversas[].pedido` do Provider (o mesmo
// lugar da comanda do Balcão), na conversa da janela: o "+" soma sempre nela, qualquer que seja a `selecionada`.

export const somarNaJanela = (acoes, janela, item) => {
  if (!janela?.conversaId || !item) return false
  acoes.adicionarItem(janela.conversaId, item)
  return true
}

export const ajustarNaJanela = (acoes, janela, sku, delta) => {
  if (!janela?.conversaId) return false
  acoes.ajustarQuantidade(janela.conversaId, sku, delta)
  return true
}

// Registrar: `gerarPedido` devolve `{ erro }` na recusa. Sucesso fecha a janela e apaga o rascunho; falha mantém tudo.
export async function registrarDaJanela({ acoes, janela, meio, enviarAoCliente = true, aoRegistrar }) {
  if (!janela?.conversaId) return { erro: 'Abra o Novo pedido de novo.' }
  const resposta = await acoes.gerarPedido(janela.conversaId, meio ?? null, enviarAoCliente)
  // #2978: o erro aparece uma vez, junto do Registrar; o aviso do provedor (e a barra de cima) repetiria o mesmo texto.
  if (resposta?.erro) acoes.fecharAvisoApi?.()
  if (resposta?.erro) return { erro: faltaEndereco({ status: 409, message: resposta.erro }) ? `${resposta.erro} Volte ao passo Entrega e pagamento e cadastre o endereço.` : resposta.erro }
  // `criarPedido` devolve undefined quando não há pedido a criar ou a conversa já tem um: nada foi registrado.
  if (!resposta) return { erro: 'Não consegui confirmar o pedido. Confira em Pedidos antes de tentar de novo; a montagem foi mantida.' }
  aoRegistrar?.(resposta)
  return { ok: true, pedido: resposta }
}

// Conversa com pedido ainda em andamento: o Registrar não criaria outro, então a janela avisa na abertura em vez de
// deixar montar. Pedido entregue ou cancelado não trava (o backend não limpa `PedidoEmAndamentoId` nesse caso).
export const pedidoJaCriadoNaConversa = (conversa) => Boolean(conversa?.pedido?.pedidoId) && !pedidoEncerrado(conversa.pedido)
// Pedido anterior já encerrado: a janela começa comanda nova pelo mesmo `novaComanda` do Atendimento (só da tela;
// o pedido nasce no EasyStok no Registrar).
export const precisaNovaComanda = (conversa) => Boolean(conversa?.pedido?.pedidoId) && pedidoEncerrado(conversa.pedido)
// Comanda em montagem que já estava na conversa (Atendimento/Balcão): não é rascunho da janela.
export const comandaEmAndamento = (conversa) => (conversa?.pedido?.itens?.length ?? 0) > 0 && !pedidoEncerrado(conversa.pedido)
// #2941: comanda "de fora" é a que já existia ao abrir ou surgiu por fora (outra atendente) e a janela NÃO montou. É derivada a cada
// render: `montouNaJanela` vira true quando a própria janela soma itens (clique no "+" ou restauração do rascunho), e aí os itens
// são rascunho dela (grava rascunho, mostra Descartar). Limite conhecido: itens que chegam por fora depois de a janela já ter
// montado os seus entram na conta dela, porque o estado não diz de quem é cada linha.
export const comandaDeFora = (conversa, montouNaJanela) => comandaEmAndamento(conversa) && !montouNaJanela
// #2941: o "+" sobre uma comanda de fora soma nela, mas não a torna rascunho da janela (senão o Descartar apagaria os itens
// que já existiam). Só marca a montagem quando a janela começa do vazio ou já montou.
export const somaEhDaJanela = (conversa, montouNaJanela) => !comandaDeFora(conversa, montouNaJanela)
// #2941: no Registrar, o POST cria o pedido e a recarga o põe na conversa antes de a janela fechar; nesse intervalo o aviso de
// "pedido em andamento" não pode tomar o lugar da janela (pisca). Só aparece quando não há registro em curso.
export const mostrarAvisoDePedidoEmAndamento = ({ jaTemPedido, registrando }) => Boolean(jaTemPedido) && !registrando
// #2941: o endereço "cadastrado" só vale como confirmação enquanto o campo ainda mostra o que foi salvo; editou, a linha some.
export const enderecoVigente = (cadastrado, texto) => (cadastrado && cadastrado === (texto ?? '').trim() ? cadastrado : null)
// #3076: o dossiê chega depois de a janela abrir; o endereço do cadastro preenche o campo só enquanto ninguém digitou.
export const enderecoQueChegou = (digitado, doCadastro) => (!String(digitado ?? '').trim() && doCadastro ? doCadastro : null)
// Sem cardápio carregado todo item pareceria "fora do cardápio".
export const cardapioPronto = (cardapio) => Array.isArray(cardapio) && cardapio.length > 0
// #3076: "carregando" só até a primeira leitura. Leu e veio vazio é pronto (a janela diz que não há item); a recusa
// (vitrine desligada, falha) diz o motivo em vez de esperar para sempre. Itens já na tela valem mesmo com falha depois.
export function situacaoDoCardapio(catalogo) {
  if (cardapioPronto(catalogo?.cardapio)) return { tipo: 'pronto' }
  if (catalogo?.cardapioFalha) return { tipo: 'falhou', mensagem: catalogo.cardapioFalha }
  return catalogo?.cardapioLido ? { tipo: 'pronto' } : { tipo: 'carregando' }
}
// #3154: com a loja online desligada não há item para montar; o botão Novo pedido diz antes de abrir a janela.
export const motivoDoNovoPedido = (catalogo) => {
  const leitura = situacaoDoCardapio(catalogo)
  return leitura.tipo === 'falhou' && leitura.mensagem === AVISO_LOJA_DESLIGADA ? AVISO_LOJA_DESLIGADA : null
}
// #3154: sem item, o motivo curto é o do cardápio quando ele não veio; "Some ao menos um item" só com o cardápio na tela.
// A frase inteira fica no corpo da etapa Itens: o motivo aparece de novo no rodapé da janela.
function motivoSemItens(cardapio) {
  if (cardapio?.tipo === 'carregando') return 'Carregando o cardápio…'
  if (cardapio?.tipo === 'falhou') return cardapio.mensagem === AVISO_LOJA_DESLIGADA ? 'Loja online desligada' : 'O cardápio não carregou'
  return 'Some ao menos um item'
}

// Entrega e endereço (como a comanda): a API usa o endereço do cadastro do cliente. Sem ele, 409 na simulação e no
// Registrar. O caminho é cadastrar o endereço aqui, pelo mesmo `editarDadoCliente('endereco')` da Ficha.
export const faltaEndereco = (erro) => erro?.status === 409 && /endere[cç]o/i.test(erro.message ?? '')

// #2974: CEP fora da área (sem zona ativa nem raio) volta como `freteACombinar`; sem o frete combinado pela equipe o
// Registrar recebe 422. Zero vale (frete grátis combinado).
export const faltaFreteCombinado = (simulacao, pedido) => Boolean(simulacao?.freteACombinar) && pedido?.freteCombinado == null

export const podeAvancarDaEntrega = ({ pedido, entrega = 'entrega', erroSimulacao = null, simulacao = null }) =>
  Boolean(pedido?.janela && pedido?.meio) && !(entrega !== 'retirada' && faltaEndereco(erroSimulacao))
  && !faltaFreteCombinado(simulacao, pedido)

// Texto claro para qualquer recusa da API; a de endereço diz onde resolver.
// #3076: no próprio passo, aponta o campo em vez de mandar ir ao passo em que a pessoa já está.
export function textoDoErro(erro, { noPassoDaEntrega = false } = {}) {
  const texto = erro?.message || 'Não foi possível calcular o pedido. Tente de novo.'
  if (!faltaEndereco(erro)) return texto
  return noPassoDaEntrega ? `${texto} Use o campo Endereço de entrega acima.` : `${texto} Cadastre o endereço no passo Entrega e pagamento.`
}
// #3076: com a recusa do formato já no campo, a falta de endereço da simulação não repete o aviso embaixo.
export const erroDaSimulacaoNaEntrega = (erro, erroDoCampo) => (erroDoCampo && faltaEndereco(erro) ? null : erro ?? null)

export async function cadastrarEnderecoDaJanela({ acoes, conversa, texto }) {
  const endereco = (texto ?? '').trim()
  if (!endereco) return { erro: 'Escreva o endereço de entrega.' }
  try {
    const r = await acoes.editarDadoCliente(conversa.id, 'endereco', endereco, undefined, identidadeDaEdicao(conversa))
    return r?.erro ? { erro: r.erro } : { ok: true }
  } catch (falha) {
    return { erro: falha.message || 'Não foi possível cadastrar o endereço. Tente de novo.' }
  }
}

// #2944 (E1-21): o resumo sai pelo WhatsApp; sem celular (nem o número do canal) a cliente não é avisada. A loja precisa saber antes de registrar.
export const avisoSemCelular = (conversa, enviarAoCliente) => {
  if (!enviarAoCliente) return null
  const cliente = conversa?.cliente ?? {}
  return cliente.telefone || (conversa?.canal === 'WhatsApp' && cliente.telefoneCanal) ? null : 'A cliente não será avisada: falta o celular'
}

// Atalho do aviso: grava o celular no cadastro pelo mesmo `editarDadoCliente('telefone')` da Ficha, com a regra do #2930.
export async function cadastrarCelularDaJanela({ acoes, conversa, texto }) {
  const erro = erroDoCelular(texto)
  if (erro) return { erro }
  try {
    const r = await acoes.editarDadoCliente(conversa.id, 'telefone', telefoneNaTela(texto), undefined, identidadeDaEdicao(conversa))
    return r?.erro ? { erro: r.erro } : { ok: true }
  } catch (falha) {
    return { erro: falha.message || 'Não foi possível gravar o celular. Tente de novo.' }
  }
}

export function rascunhoDaMontagem(janela, pedido, passo = 'itens', cardapio = [], { entrega, endereco, enderecoCadastrado } = {}) {
  if (!janela?.clienteId || !pedido?.itens?.length) return null
  return {
    clienteId: janela.clienteId,
    nome: janela.nome ?? '',
    passo,
    itens: pedido.itens.map((l) => ({ sku: l.sku, qtd: l.qtd, obs: l.obs ?? '', nome: itemPorSku(cardapio, l.sku)?.nome ?? null })),
    janela: pedido.janela ?? null,
    meio: pedido.meio ?? null,
    entrega: entrega ?? pedido.entrega ?? 'entrega',
    endereco: endereco ?? null,
    enderecoCadastrado: enderecoCadastrado ?? null,
  }
}

// Reabrir: refaz a montagem com o cardápio do dia. Item que saiu do cardápio (ou ficou indisponível) sai com aviso.
export function restaurarRascunho(rascunho, cardapio) {
  const itens = []
  const avisos = []
  for (const linha of rascunho?.itens ?? []) {
    const prato = itemPorSku(cardapio, linha.sku)
    if (!prato || prato.removidoEm || !disponivelHoje(prato)) {
      avisos.push(`${prato?.nome ?? linha.nome ?? 'Um item'} saiu do cardápio e foi tirado do pedido`)
    } else itens.push(linha)
  }
  return { itens, avisos, passo: rascunho?.passo ?? 'itens', janela: rascunho?.janela ?? null, meio: rascunho?.meio ?? null,
    entrega: rascunho?.entrega ?? 'entrega', endereco: rascunho?.endereco ?? null,
    enderecoCadastrado: rascunho?.enderecoCadastrado ?? null }
}

export const novoPedidoDisponivel = ({ abrirConversaDoCliente, permite, acoes }) =>
  Boolean(abrirConversaDoCliente) && permite('atendimento') && acoes?.atenderConversas === true

// Rótulo do botão de entrada: com rascunho guardado da cliente, "Continuar pedido"; sem cliente conhecida, "Novo pedido".
export const rotuloDoNovoPedido = (sessao, clienteId) => (temRascunhoDoPedido(sessao, clienteId) ? 'Continuar pedido' : 'Novo pedido')

// #2944 (E1-01): por que Continuar/Registrar está parado. `null` quando nada bloqueia (mesma regra de `podeAvancarDaEntrega`).
// #2978: o Revisar repete as regras da Entrega e ainda exige endereço válido (`enderecoValido`), para não registrar às cegas.
export function motivoDeBloqueio({ passo, itens = [], pedido = null, entrega = 'entrega', erroSimulacao = null, simulacao = null, enderecoValido = true, calculando = false, cardapio = null }) {
  if (itens.length === 0) return motivoSemItens(cardapio)
  if (passo === 'itens') return null
  if (!pedido?.janela) return 'Escolha a janela de entrega'
  if (!pedido?.meio) return 'Escolha a forma de pagamento'
  // #3076: com o frete ainda calculando, o total não fecha; Continuar e Registrar esperam a resposta.
  if (calculando) return 'Calculando o frete…'
  if (entrega !== 'retirada' && passo === 'revisar' && !enderecoValido) return 'Cadastre o endereço de entrega: o pedido usa o do cadastro, não o digitado'
  if (entrega !== 'retirada' && faltaEndereco(erroSimulacao)) return 'Falta o endereço de entrega'
  if (faltaFreteCombinado(simulacao, pedido)) return 'Informe o frete combinado'
  return null
}

// #2944 (E1-03): faixa fixa do rodapé da janela, em todos os passos. `total` já vem formatado.
export function textoDaFaixa(itens, total) {
  const qtd = (itens ?? []).reduce((soma, l) => soma + (l.qtd ?? 0), 0)
  if (qtd === 0) return 'Nenhum item ainda'
  return `${qtd} ${qtd === 1 ? 'item' : 'itens'} · ${total}`
}
