import { Avatar } from '../../componentes/Avatar'
import { Icone } from '../../componentes/Icone'
import { useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { sinalVerde } from '../../dominio/cobranca'
import { ehLead, previaDaConversa, tagCorrespondida } from '../../dominio/conversa'
import { motivoDePrecisar } from '../../dominio/automatico'
import { contextoDaLinha, estadoDaLinha, pendentesDaLinha } from '../../dominio/linhaDoBalcao'
import { origemDaPassagem } from '../../dominio/passagem'
import { canalDaConversa, fotoDoCliente } from '../../dominio/canal'
import { estadoDaJanela } from '../../dominio/janela'
import { passoPorId } from '../../dominio/esteira'
import { inicioDoPedidoAtivo } from '../../dominio/entrega'
import { duracao, horaMono } from '../../dominio/formato'
import css from './caixa.module.css'

// Rótulo e ícone do motivo de precisar, por `chave` (dominio/automatico.js).
// A direção 19 (seção 1) pede um texto de linha diferente do `rotulo` que o
// domínio usa em outro lugar ("Precisa de você" é o nome do filtro, não do
// estado de quem passou para ela): a apresentação mora aqui, o domínio
// continua falando só com quem já falava com ele.
const APRESENTACAO_DO_MOTIVO = {
  reclamacao: { texto: 'Reclamação', icone: 'message-square-warning' },
  passagem: { texto: 'Passou para você', icone: 'hand' },
  esperando: { texto: 'Esperando você', icone: 'relogio' },
  cobranca: { texto: 'Cobrança venceu', icone: 'dollar-sign' },
  // #1474 (R12): o cliente escreveu fora do horário; "Loja abriu" parecia aviso da loja.
  'aguardando-abertura': { texto: 'Escreveu com a loja fechada', icone: 'relogio' },
  // Rodada 10, itens 3 e 4: pedido atrasado (ainda na cozinha) e entrega
  // atrasada (já em rota) ganham marca própria, mesmo padrão das outras.
  'pedido-atrasado': { texto: 'Pedido atrasado', icone: 'cooking-pot' },
  'atraso-entrega': { texto: 'Entrega atrasada', icone: 'moto' },
}

const ICONE_DO_PASSO = {
  aguardando: 'dollar-sign', pago: 'circle-check', preparo: 'cooking-pot',
  embalado: 'package', entrega: 'moto', entregue: 'house',
}

// Passo do pedido no contexto da linha (com o sinal verde do pagamento, RN-25,
// dez minutos só) ou "Lead" quando ainda não há pedido nem cadastro.
function marcaDoPedido(conversa) {
  const passo = conversa.pedido ? passoPorId(conversa.pedido.estado) : null
  if (passo) {
    if (sinalVerde(conversa.pedido)) return { texto: 'Pago', icone: 'circle-check', tom: 'ok' }
    return { texto: passo.rotulo, icone: ICONE_DO_PASSO[passo.id], tom: 'neutro' }
  }
  if (ehLead(conversa)) return { texto: 'Lead', icone: 'user-plus', tom: 'ragu' }
  return null
}

// DEN-06: cobrança vencida repete o aviso do pedido e do lembrete de
// pagamento em outro lugar da tela, então não pede o vermelho de alarme aqui
// dentro: vira estado neutro, com o rótulo escrito.
function motivoDaLinha(motivo) {
  if (!motivo) return null
  const apresentacao = APRESENTACAO_DO_MOTIVO[motivo.chave]
  return {
    ...motivo,
    tom: motivo.chave === 'cobranca' ? 'neutro' : motivo.tom,
    rotulo: apresentacao?.texto ?? motivo.rotulo,
    icone: apresentacao?.icone ?? null,
  }
}

// Linha da conversa (#1442, homologação de 07/10): avatar com o canal no
// selo; nome e hora; prévia em largura cheia com quantas mensagens esperam
// resposta; e, miúdo, quem está com a conversa (ou o motivo de precisar dela)
// seguido do contexto que existir (origem da passagem, pedido, entrega,
// janela, tag).
export function CartaoConversa({
  conversa, selecionada, agora, automaticoPausado, aberta = true, grupo, busca, aoAbrir,
}) {
  const { canais, janelas } = useCatalogo()
  const { pagamentosNaoVistos } = useAtendimento()
  const canal = canalDaConversa(canais, conversa)
  const pausado = automaticoPausado?.[conversa.id] ?? false
  const motivo = grupo ? null : motivoDaLinha(motivoDePrecisar(conversa, agora, pausado, aberta, janelas))
  const estado = estadoDaLinha(conversa, { pausado, grupo, motivo })
  // Rodada 12 (issue #16): "Passou para você" sem origem obrigava a abrir a
  // conversa para saber por quê. O contexto diz quem passou, a hora e o motivo.
  const origem = motivo?.chave === 'passagem' ? origemDaPassagem(conversa.passagem) : null
  // Item 2 (rodada 10, achado 4): selo "novo, não visto" do pagamento,
  // independente do sinal verde (RN-25) já ter expirado.
  const pagamentoNaoVisto = pagamentosNaoVistos?.has(conversa.id) ?? false

  // Janela de 24 h fechada (só modelo) vence a hora do pedido; a tag que achou
  // só aparece quando o nome sozinho não explicaria a busca (US-016).
  const estadoJanela = canal.temJanela ? estadoDaJanela(conversa, agora, canal) : null
  const janelaFechada = estadoJanela ? estadoJanela.restam <= 0 : false
  const inicioPedido = !janelaFechada ? inicioDoPedidoAtivo(conversa, janelas, agora) : null
  const nomeCombinaBusca = busca?.trim() && conversa.nome.toLowerCase().includes(busca.trim().toLowerCase())
  const tagAchada = busca?.trim() && !nomeCombinaBusca ? tagCorrespondida(conversa, busca) : null
  const contexto = contextoDaLinha({
    origem: origem?.curto ?? null,
    pedido: grupo ? null : marcaDoPedido(conversa),
    entregaAte: inicioPedido != null ? horaMono(inicioPedido) : null,
    janelaFechada,
    tagAchada,
  })
  const previa = previaDaConversa(conversa)
  // Mensagens do cliente depois da última resposta de verdade (issue #41):
  // aviso de esteira e nota não zeram a contagem.
  const pendentes = pendentesDaLinha(conversa)

  return (
    <button
      type="button"
      className={[
        css.cartao,
        motivo?.tom === 'aviso' ? css.precisa : '',
        motivo?.tom === 'perigo' ? css.atrasada : '',
      ].filter(Boolean).join(' ')}
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
      </span>
      <span className={css.corpo}>
        <span className={css.topo}>
          <strong className={pendentes > 0 ? css.semResposta : ''} title={conversa.nome}>
            {conversa.nome}
          </strong>
          {selecionada && <span className="sr">, conversa aberta</span>}
          {pagamentoNaoVisto && (
            <span className={css.pontoNaoVisto} title="Pagamento novo, ainda não visto" aria-label="Pagamento novo, ainda não visto" />
          )}
          <time dateTime={conversa.ultimaEm} className={pendentes > 0 ? css.horaNova : ''}>
            {duracao(agora - new Date(conversa.ultimaEm).getTime())}
          </time>
        </span>
        <span className={css.linhaPrevia}>
          <span className={css.previa}>
            {/* Quem escreveu por último: "Você:" e "Sistema:" por extenso;
                o automático vira o raio, já que a linha do estado diz a palavra. */}
            {previa.prefixo && (
              <span className={css.prefixo}>
                {previa.prefixo.startsWith('Automático')
                  ? <><Icone nome="raio" tamanho={14} /><span className="sr">{previa.prefixo}</span></>
                  : previa.prefixo.trim()}
              </span>
            )}
            {previa.corpo}
          </span>
          {pendentes > 0 && (
            <span
              className={css.naoLidas}
              aria-label={pendentes === 1 ? '1 mensagem sem resposta' : `${pendentes} mensagens sem resposta`}
              title={pendentes === 1 ? '1 mensagem sem resposta' : `${pendentes} mensagens sem resposta`}
            >
              {pendentes}
            </span>
          )}
        </span>
        <span className={css.contextoLinha} title={[estado.texto, ...contexto.map((item) => item.texto)].join(' · ')}>
          <span className={`${css.estado} ${css['estado_' + estado.tom]}`} title={estado.titulo ?? undefined}>
            {estado.icone && <Icone nome={estado.icone} tamanho={14} />}
            {estado.texto}
          </span>
          {contexto.map((item) => (
            <span key={item.chave} className={`${css.itemContexto} ${css['contexto_' + item.tom] ?? ''}`}>
              {item.icone && <Icone nome={item.icone} tamanho={14} />}
              <span className={css.textoContexto}>{item.texto}</span>
            </span>
          ))}
        </span>
      </span>
    </button>
  )
}
