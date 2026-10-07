import { Avatar } from '../../componentes/Avatar'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { sinalVerde } from '../../dominio/cobranca'
import { ehLead, previaDaConversa, tagCorrespondida } from '../../dominio/conversa'
import { motivoDePrecisar, respostaAtrasada } from '../../dominio/automatico'
import { mensagensSemRespostaReal } from '../../dominio/mensagem'
import { origemDaPassagem } from '../../dominio/passagem'
import { canalDaConversa, fotoDoCliente } from '../../dominio/canal'
import { estadoDaJanela } from '../../dominio/janela'
import { passoPorId } from '../../dominio/esteira'
import { inicioDoPedidoAtivo } from '../../dominio/entrega'
import { duracao, horaMono } from '../../dominio/formato'
import css from './caixa.module.css'
import { classesDoCartao } from './classesDoCartao'

// Rótulo e ícone da marca única, por `chave` do motivo (dominio/automatico.js).
// A direção 19 (seção 1) pede um texto de pílula diferente do `rotulo` que o
// domínio usa em outro lugar ("Precisa de você" é o nome da aba, não da
// pílula de quem passou para ela): a apresentação mora aqui, o domínio
// continua falando só com quem já falava com ele.
const APRESENTACAO_DO_MOTIVO = {
  reclamacao: { texto: 'Reclamação', icone: 'message-square-warning' },
  passagem: { texto: 'Passou para você', icone: 'hand' },
  esperando: { texto: 'Esperando você', icone: 'relogio' },
  cobranca: { texto: 'Cobrança venceu', icone: 'dollar-sign' },
  'aguardando-abertura': { texto: 'Loja abriu', icone: 'relogio' },
  // Rodada 10, itens 3 e 4: pedido atrasado (ainda na cozinha) e entrega
  // atrasada (já em rota) ganham marca própria, mesmo padrão das outras.
  'pedido-atrasado': { texto: 'Pedido atrasado', icone: 'cooking-pot' },
  'atraso-entrega': { texto: 'Entrega atrasada', icone: 'moto' },
}

const ICONE_DO_PASSO = {
  aguardando: 'dollar-sign', pago: 'circle-check', preparo: 'cooking-pot',
  embalado: 'package', entrega: 'moto', entregue: 'house',
}

// UMA marca por cartão, por precedência (a da rodada 3 continua, só mudam
// rótulo e ícone): bloqueado e encerrado vencem tudo, mas só existem dentro do
// próprio grupo recolhido — a lista de cima nunca traz essas duas conversas.
// Dentro dela: motivo de precisar > passo da esteira (com o sinal verde do
// pagamento) > lead sem pedido > nada, porque "Aberto"/"Em atendimento" sem
// pendência não é pílula (seção 1, corte #10).
function marcaDe(conversa, motivo, grupo) {
  if (grupo === 'bloqueados') return { tom: 'perigo', texto: 'Bloqueado', icone: 'lock' }
  if (grupo === 'encerradas') return { tom: 'neutro', texto: 'Encerrada', icone: 'log-out' }
  if (motivo) {
    const apresentacao = APRESENTACAO_DO_MOTIVO[motivo.chave]
    return { tom: motivo.tom, texto: apresentacao?.texto ?? motivo.rotulo, icone: apresentacao?.icone }
  }
  const passo = conversa.pedido ? passoPorId(conversa.pedido.estado) : null
  if (passo) {
    // Sinal verde do pagamento (RN-25): mesma marca do passo, em verde, dez
    // minutos só.
    if (sinalVerde(conversa.pedido)) return { tom: 'ok', texto: 'Pago', icone: 'circle-check' }
    return { tom: passo.tom, texto: passo.rotulo, icone: ICONE_DO_PASSO[passo.id] }
  }
  if (ehLead(conversa)) return { tom: 'ragu', texto: 'Lead', icone: 'user-plus' }
  return null
}

// DEN-06: cobrança vencida repete o aviso do pedido e do lembrete de
// pagamento em outro lugar da tela, então não pede o vermelho de alarme aqui
// dentro: vira marca neutra, com o rótulo escrito.
function motivoDoCartao(motivo) {
  if (motivo?.chave !== 'cobranca') return motivo
  return { ...motivo, tom: 'neutro' }
}

export function CartaoConversa({
  conversa, selecionada, agora, automaticoPausado, aberta = true, expediente = null, grupo, busca, aoAbrir,
}) {
  const { canais, janelas } = useCatalogo()
  const { pagamentosNaoVistos } = useAtendimento()
  const canal = canalDaConversa(canais, conversa)
  const pausado = automaticoPausado?.[conversa.id] ?? false
  const motivo = grupo ? null : motivoDoCartao(motivoDePrecisar(conversa, agora, pausado, aberta, janelas, expediente))
  // #1427: SLA de primeira resposta da loja estourado, o cartão pisca.
  const slaEstourado = !grupo && respostaAtrasada(conversa, agora, pausado, expediente)
  const marca = marcaDe(conversa, motivo, grupo)
  // Rodada 12 (issue #16): "Passou para você" sem origem obrigava a abrir a
  // conversa para saber por quê. A linha diz quem passou, a hora e o motivo.
  const origem = motivo?.chave === 'passagem' ? origemDaPassagem(conversa.passagem) : null
  // Item 2 (rodada 10, achado 4): selo "novo, não visto" do pagamento,
  // independente do sinal verde (RN-25) já ter expirado.
  const pagamentoNaoVisto = pagamentosNaoVistos?.has(conversa.id) ?? false

  // Contexto por precedência (seção 1): 1º "Só modelo" quando a janela de 24 h
  // fechou, 2º a hora do pedido ainda ativo, 3º a tag que achou (US-016,
  // só quando o nome sozinho não explicaria), senão vazio.
  const estadoJanela = canal.temJanela ? estadoDaJanela(conversa, agora, canal) : null
  const janelaFechada = estadoJanela ? estadoJanela.restam <= 0 : false
  const inicioPedido = !janelaFechada ? inicioDoPedidoAtivo(conversa, janelas, agora) : null
  const nomeCombinaBusca = busca?.trim() && conversa.nome.toLowerCase().includes(busca.trim().toLowerCase())
  const tagAchada = busca?.trim() && !nomeCombinaBusca ? tagCorrespondida(conversa, busca) : null
  const previa = previaDaConversa(conversa)
  // Mensagens do cliente depois da última resposta de verdade: o número do
  // contador. `mensagensSemRespostaReal` (issue #41) ignora aviso de esteira
  // e nota de sistema no meio do caminho: eles não respondem o cliente, então
  // não podem zerar a contagem só por virarem a última mensagem da lista.
  const esperando = mensagensSemRespostaReal(conversa).length

  return (
    <button
      type="button"
      className={classesDoCartao(css, { motivo, slaEstourado })}
      aria-current={selecionada}
      onClick={() => aoAbrir(conversa.id)}
    >
      <span className={css.avatarCartao}>
        <Avatar
          nome={conversa.nome}
          foto={fotoDoCliente(canal, conversa.cliente)}
          tamanho="grande"
          selo={{ titulo: canal.nome, icone: <Icone nome={canal.icone} tamanho={12} className={css['canal_' + canal.icone]} /> }}
        />
        {conversa.mensagens.at(-1)?.dir === 'in' && <span className={css.naoLida} aria-hidden="true" />}
      </span>
      <span className={css.corpo}>
        <span className={css.topo}>
          <strong
            className={conversa.mensagens.at(-1)?.dir === 'in' ? css.semResposta : ''}
            title={conversa.nome}
          >
            {conversa.nome}
          </strong>
          {selecionada && <span className="sr">, conversa aberta</span>}
          {slaEstourado && <span className="sr">, prazo de resposta estourado</span>}
          <time dateTime={conversa.ultimaEm}>
            {duracao(agora - new Date(conversa.ultimaEm).getTime())}
          </time>
        </span>
        <span className={css.previa}>
          {previa.prefixo && (
            <span className={css.prefixo}>
              {previa.prefixo.startsWith('Automático') && <Icone nome="raio" tamanho={14} />}
              {previa.prefixo.startsWith('Você') && <Icone nome="arrow-up" tamanho={14} />}
              <span className={previa.prefixo.startsWith('Sistema') ? undefined : 'sr'}>{previa.prefixo}</span>
            </span>
          )}
          {previa.corpo}
        </span>
        <span className={css.marcas}>
          {marca ? (
            <span title={motivo?.texto} className={css.marcaComMotivo}>
              <Pilula tom={marca.tom} icone={marca.icone}>{marca.texto}</Pilula>
            </span>
          ) : null}
          {/* Rodada 12 (issue #16): o número num círculo ao lado da hora era o
              "alertazinho" que ninguém sabia ler. Agora é escrito. */}
          {esperando > 0 && (
            <span className={`${css.contexto} ${css.contagemSemResposta}`}>
              <Icone nome="conversa" tamanho={14} />
              {esperando === 1 ? '1 mensagem sem resposta' : `${esperando} mensagens sem resposta`}
            </span>
          )}
          {pagamentoNaoVisto && (
            <span className={css.pontoNaoVisto} title="Pagamento novo, ainda não visto" aria-label="Pagamento novo, ainda não visto" />
          )}
          {janelaFechada && (
            <span className={`${css.contexto} ${css.soModelo}`} title={estadoJanela.rotulo}>
              <Icone nome="lock" tamanho={14} /> Só modelo
            </span>
          )}
          {inicioPedido != null && !janelaFechada && (
            <span className={css.contexto}>
              <Icone nome="moto" tamanho={18} />
              {' até '}
              <span className={css.horaContexto}>{horaMono(inicioPedido)}</span>
            </span>
          )}
          {!janelaFechada && inicioPedido == null && tagAchada && (
            <span className={css.contexto} title={`Achado pela tag "${tagAchada}"`}>
              Tag: {tagAchada}
            </span>
          )}
        </span>
        {origem && (
          <span className={css.origemPassagem}>{origem.curto}</span>
        )}
      </span>
    </button>
  )
}
