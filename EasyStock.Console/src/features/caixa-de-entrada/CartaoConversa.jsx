import { memo } from 'react'
import { AssuntoEmail } from '../../componentes/AssuntoEmail'
import { NomeDoContato } from '../../componentes/NomeDoContato'
import { Avatar } from '../../componentes/Avatar'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { useCatalogo } from '../../aplicacao/contextos'
import { useFotoCliente } from '../../aplicacao/useFotoCliente'
import { previaDaConversa, tagCorrespondida } from '../../dominio/conversa'
import { motivoDePrecisar, respostaAtrasada } from '../../dominio/automatico'
import { contextoDaLinha, estadoDaLinha, pendentesDaLinha } from '../../dominio/linhaDoBalcao'
import { canalDaConversa, fotoDoCliente } from '../../dominio/canal'
import { estadoDaJanela } from '../../dominio/janela'
import { inicioDoPedidoAtivo } from '../../dominio/entrega'
import { horaMono } from '../../dominio/formato'
import { horaDaLista } from '../../dominio/horaDaLista'
import css from './caixa.module.css'
import { classesDoCartao } from './classesDoCartao'
import { marcaDoPedido, mostraEtiquetaDoEstado, motivoResumido, nomeDoCartao, origemNoCartao, rotuloDoCartao, temLinhaDeContexto } from './apresentacaoDoCartao'

// DEN-06: cobrança vencida repete o aviso do pedido e do lembrete de
// pagamento em outro lugar da tela, então não pede o vermelho de alarme aqui
// dentro: vira estado neutro, com o rótulo escrito.
function motivoDaLinha(motivo, origem) {
  if (!motivo) return null
  // #2258 (T6): rótulo de até 18 caracteres; a frase inteira segue no `title` (estado.titulo).
  const resumido = motivoResumido(motivo.chave, origem)
  return {
    ...motivo,
    tom: motivo.chave === 'cobranca' ? 'neutro' : motivo.tom,
    rotulo: resumido?.texto ?? motivo.rotulo,
    icone: resumido?.icone ?? null,
    texto: motivo.texto ?? resumido?.titulo ?? null,
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
  grupo, busca, aoAbrir, fonteApi = false, usuario = null,
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
    // #2232 (L9): no cartão vai o chip curto do motivo; a frase inteira fica na conversa.
    origem: origem?.chip ?? null,
    pedido: grupo ? null : marcaDoPedido(conversa, cardapio, { fonteApi }),
    entregaAte: inicioPedido != null ? horaMono(inicioPedido) : null,
    janelaFechada,
    tagAchada,
  })
  const previa = previaDaConversa(conversa, usuario)
  // Mensagens do cliente depois da última resposta de verdade (issue #41):
  // aviso de esteira e nota não zeram a contagem.
  const pendentes = pendentesDaLinha(conversa)
  // #1868: o genérico "Visitante do site" cede o nome à comanda; a etiqueta da passagem vira o
  // ícone ao lado da hora (#2420, sem traço lateral) e o texto fica no nome acessível. #1899: Automático e
  // Com você também saem da etiqueta (quem conduz está na BarraModo).
  const nome = nomeDoCartao(conversa)
  const etiqueta = mostraEtiquetaDoEstado(estado)
  // #2232 (L8): "21:48", "ontem", "26 out"; o alerta de cor fica só para o prazo estourado.
  const hora = horaDaLista(conversa.ultimaEm, agora)

  return (
    <button
      type="button"
      className={classesDoCartao(css, { motivo, slaEstourado })}
      aria-current={selecionada}
      aria-label={rotuloDoCartao({
        nome: nome.titulo, estado: estado.texto, pendentes, contexto: contexto.map((item) => item.texto),
        atrasada: slaEstourado, pagamentoNaoVisto, hora, selecionada,
      })}
      onClick={() => aoAbrir(conversa.id)}
    >
      <span className={css.avatarCartao}>
        <Avatar
          nome={conversa.nome}
          foto={fotoCadastrada.url ?? fotoDoCliente(canal, conversa.cliente)}
          tamanho="grande"
          selo={{ titulo: canal.nome, icone: <Icone nome={canal.icone} tamanho={16} className={css['canal_' + canal.icone]} /> }}
        />
      </span>
      <span className={css.corpo}>
        <span className={css.topo}>
          <strong className={pendentes > 0 ? css.semResposta : ''}>
            <NomeDoContato
              completo={nome.principal} curto={nome.curto} capsula={nome.capsula} titulo={nome.titulo}
              complemento={nome.complemento && <small aria-label={nome.complemento}> · {nome.complemento.replace(/^Comanda /, '#')}</small>}
            />
          </strong>
          {/* #2258 (T7): a cor da borda não é o único sinal; o prazo estourado e a vez da equipe têm ícone e texto. */}
          {slaEstourado && (
            <span className={css.sinalPrazo} title="Prazo de resposta estourado" role="img" aria-label="Prazo de resposta estourado">
              <Icone nome="relogio" tamanho={16} />
            </span>
          )}
          {!slaEstourado && motivo && !etiqueta && (
            <span className={`${css.sinalPrazo} ${css.sinalAtencao}`} title={estado.texto} role="img" aria-label={estado.texto}>
              <Icone nome={estado.icone ?? 'hand'} tamanho={16} />
            </span>
          )}
          {pagamentoNaoVisto && (
            <span className={css.pontoNaoVisto} title="Pagamento novo, ainda não visto" />
          )}
          <time dateTime={conversa.ultimaEm} className={slaEstourado ? css.horaAtrasada : ''}>
            {hora}
          </time>
        </span>
        {/* #1432: assunto do e-mail, só no canal E-mail (nos outros não desenha nada). */}
        <AssuntoEmail assunto={conversa.assunto} />
        <span className={css.linhaPrevia}>
          <span className={css.previa}>
            {/* Quem escreveu por último: "Você:" e "Sistema:" por extenso;
                o Assistente vira o raio, já que a linha do estado diz a palavra. */}
            {previa.prefixo && (
              <span className={css.prefixo}>
                {previa.prefixo.startsWith('Assistente')
                  ? <><Icone nome="sparkles" tamanho={16} /><span className="sr">{previa.prefixo}</span></>
                  : previa.prefixo.trim()}
              </span>
            )}
            {previa.corpo}
          </span>
          {pendentes > 0 && (
            <Pilula
              contagem={pendentes} tom="marca" className={css.naoLidas}
              aria-label={pendentes === 1 ? '1 mensagem sem resposta' : `${pendentes} mensagens sem resposta`}
              titulo={pendentes === 1 ? '1 mensagem sem resposta' : `${pendentes} mensagens sem resposta`}
            />
          )}
        </span>
        {temLinhaDeContexto({ etiqueta, contexto }) && <span className={css.contextoLinha} title={[etiqueta ? estado.texto : null, ...contexto.map((item) => item.texto)].filter(Boolean).join(' · ')}>
          {etiqueta && <Pilula tamanho="inline" tom={estado.tom} icone={estado.icone ?? undefined} titulo={estado.titulo ?? undefined}>{estado.texto}</Pilula>}
          {contexto.map((item) => (
            <Pilula key={item.chave} tamanho="inline" tom={item.tom ?? 'neutro'} icone={item.icone ?? undefined}>{item.texto}</Pilula>
          ))}
        </span>}
      </span>
    </button>
  )
})
