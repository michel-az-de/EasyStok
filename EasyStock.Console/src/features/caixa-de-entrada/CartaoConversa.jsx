import { memo } from 'react'
import { AssuntoEmail } from '../../componentes/AssuntoEmail'
import { Avatar } from '../../componentes/Avatar'
import { Icone } from '../../componentes/Icone'
import { useCatalogo } from '../../aplicacao/contextos'
import { useFotoCliente } from '../../aplicacao/useFotoCliente'
import { previaDaConversa, tagCorrespondida } from '../../dominio/conversa'
import { motivoDePrecisar, respostaAtrasada } from '../../dominio/automatico'
import { contextoDaLinha, estadoDaLinha, pendentesDaLinha } from '../../dominio/linhaDoBalcao'
import { canalDaConversa, fotoDoCliente } from '../../dominio/canal'
import { estadoDaJanela } from '../../dominio/janela'
import { inicioDoPedidoAtivo } from '../../dominio/entrega'
import { duracao, horaMono } from '../../dominio/formato'
import css from './caixa.module.css'
import { classesDoCartao } from './classesDoCartao'
import { marcaDoPedido, mostraEtiquetaDoEstado, nomeDoCartao, origemNoCartao, temLinhaDeContexto } from './apresentacaoDoCartao'

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

// DEN-06: cobrança vencida repete o aviso do pedido e do lembrete de
// pagamento em outro lugar da tela, então não pede o vermelho de alarme aqui
// dentro: vira estado neutro, com o rótulo escrito.
function motivoDaLinha(motivo, origem) {
  if (!motivo) return null
  const apresentacao = motivo.chave === 'passagem' && origem === 'chat-site'
    ? { texto: 'Aguardando equipe', icone: 'relogio' } : APRESENTACAO_DO_MOTIVO[motivo.chave]
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
// #1577: em `memo`, e sem ler o estado do Balcão. O que é da conversa (`pausado`, `pagamentoNaoVisto`) desce
// já resolvido, senão a mudança de UMA conversa re-renderizava a lista inteira.
export const CartaoConversa = memo(function CartaoConversa({
  conversa, selecionada, agora, pausado = false, pagamentoNaoVisto = false, aberta = true, expediente = null,
  grupo, busca, aoAbrir, fonteApi = false,
}) {
  const { canais, janelas, cardapio } = useCatalogo()
  const fotoCadastrada = useFotoCliente(conversa.cliente?.fotoUrl)
  const canal = canalDaConversa(canais, conversa)
  const motivo = grupo ? null : motivoDaLinha(motivoDePrecisar(conversa, agora, pausado, aberta, janelas, expediente), conversa.passagem?.origem)
  const slaEstourado = !grupo && respostaAtrasada(conversa, agora, pausado, expediente)
  const estado = estadoDaLinha(conversa, { pausado, grupo, motivo })
  // Rodada 12 (issue #16): "Passou para você" sem origem obrigava a abrir a
  // conversa para saber por quê. O contexto diz quem passou, a hora e o motivo.
  // #1868: "Chat do site" repetia o selo do canal no avatar em 38 de 39 cartões e sai daqui.
  const origem = motivo?.chave === 'passagem' ? origemNoCartao(conversa.passagem) : null
  // Item 2 (rodada 10, achado 4): selo "novo, não visto" do pagamento (`pagamentoNaoVisto`),
  // independente do sinal verde (RN-25) já ter expirado.

  // Janela de 24 h fechada (só modelo) vence a hora do pedido; a tag que achou
  // só aparece quando o nome sozinho não explicaria a busca (US-016).
  const estadoJanela = canal.temJanela ? estadoDaJanela(conversa, agora, canal) : null
  const janelaFechada = estadoJanela ? estadoJanela.restam <= 0 : false
  const inicioPedido = !janelaFechada ? inicioDoPedidoAtivo(conversa, janelas, agora) : null
  const nomeCombinaBusca = busca?.trim() && conversa.nome.toLowerCase().includes(busca.trim().toLowerCase())
  const tagAchada = busca?.trim() && !nomeCombinaBusca ? tagCorrespondida(conversa, busca) : null
  const contexto = contextoDaLinha({
    origem: origem?.curto ?? null,
    pedido: grupo ? null : marcaDoPedido(conversa, cardapio, { fonteApi }),
    entregaAte: inicioPedido != null ? horaMono(inicioPedido) : null,
    janelaFechada,
    tagAchada,
  })
  const previa = previaDaConversa(conversa)
  // Mensagens do cliente depois da última resposta de verdade (issue #41):
  // aviso de esteira e nota não zeram a contagem.
  const pendentes = pendentesDaLinha(conversa)
  // #1868: o genérico "Visitante do site" cede o nome à comanda; a etiqueta da passagem vira o
  // trilho lateral do cartão (`.precisa`) e o texto fica para o leitor de tela. #1899: Automático e
  // Com você também saem da etiqueta (quem conduz está na BarraModo).
  const nome = nomeDoCartao(conversa)
  const etiqueta = mostraEtiquetaDoEstado(estado)

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
          foto={fotoCadastrada.url ?? fotoDoCliente(canal, conversa.cliente)}
          tamanho="grande"
          selo={{ titulo: canal.nome, icone: <Icone nome={canal.icone} tamanho={12} className={css['canal_' + canal.icone]} /> }}
        />
      </span>
      <span className={css.corpo}>
        <span className={css.topo}>
          <strong className={pendentes > 0 ? css.semResposta : ''} title={nome.titulo}>
            {nome.principal}{nome.complemento && <small aria-label={nome.complemento}> · {nome.complemento.replace(/^Comanda /, '#')}</small>}
          </strong>
          {!etiqueta && <span className="sr">, {estado.texto}</span>}
          {selecionada && <span className="sr">, conversa aberta</span>}
          {slaEstourado && <span className="sr">, prazo de resposta estourado</span>}
          {pagamentoNaoVisto && (
            <span className={css.pontoNaoVisto} title="Pagamento novo, ainda não visto" aria-label="Pagamento novo, ainda não visto" />
          )}
          <time dateTime={conversa.ultimaEm} className={pendentes > 0 ? css.horaNova : ''}>
            {duracao(agora - new Date(conversa.ultimaEm).getTime())}
          </time>
        </span>
        {/* #1432: assunto do e-mail, só no canal E-mail (nos outros não desenha nada). */}
        <AssuntoEmail assunto={conversa.assunto} />
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
        {temLinhaDeContexto({ etiqueta, contexto }) && <span className={css.contextoLinha} title={[etiqueta ? estado.texto : null, ...contexto.map((item) => item.texto)].filter(Boolean).join(' · ')}>
          {etiqueta && <span className={`${css.estado} ${css['estado_' + estado.tom]}`} title={estado.titulo ?? undefined}>
            {estado.icone && <Icone nome={estado.icone} tamanho={14} />}
            {estado.texto}
          </span>}
          {contexto.map((item) => (
            <span key={item.chave} className={`${css.itemContexto} ${css['contexto_' + item.tom] ?? ''}`}>
              {item.icone && <Icone nome={item.icone} tamanho={14} />}
              <span className={css.textoContexto}>{item.texto}</span>
            </span>
          ))}
        </span>}
      </span>
    </button>
  )
})
