import { Icone } from '../../componentes/Icone'
import { horaCurta } from '../../dominio/formato'
import { partesComLinkDoCardapio } from '../../dominio/cardapioLink'
import { BolhaArquivo, BolhaAudio, BolhaPeca } from './BolhaAnexo'
import { BolhaAvaliacaoPedido, BolhaAvaliacaoResposta } from './BolhaAvaliacao'
import css from './atendimento.module.css'

const CLASSE = { in: css.entrada, out: css.saida }

function Corpo({ mensagem }) {
  if (mensagem.formato === 'figurinha') {
    return <img className={css.figurinha} src={mensagem.arte} alt={mensagem.texto} />
  }
  if (mensagem.formato === 'imagem') {
    return (
      <figure className={css.figura}>
        <img src={mensagem.arte} alt={mensagem.texto} />
        <figcaption>{mensagem.texto}</figcaption>
      </figure>
    )
  }
  // Rodada 7 · frente Anexos (pedido do dono, 24/09/2026 04h12): arquivo
  // anexado, áudio (gravado ou recebido) e peça da galeria. Conteúdo mora em
  // `BolhaAnexo.jsx`, ao lado deste arquivo: feature nenhuma importa outra
  // feature (verificar-camadas.mjs).
  if (mensagem.formato === 'arquivo') return <BolhaArquivo mensagem={mensagem} />
  if (mensagem.formato === 'audio') return <BolhaAudio mensagem={mensagem} />
  if (mensagem.formato === 'peca') return <BolhaPeca mensagem={mensagem} />
  // Rodada 10 (registro 79) · US-046, RN-34, RN-35: avaliação de um toque,
  // bolha própria, nunca texto livre (achado P2.7).
  if (mensagem.formato === 'avaliacaoPedido') return <BolhaAvaliacaoPedido mensagem={mensagem} />
  if (mensagem.formato === 'avaliacaoResposta') return <BolhaAvaliacaoResposta mensagem={mensagem} />
  return (
    <p className={mensagem.regra === 'resumo' ? css.resumo : undefined}>
      {partesComLinkDoCardapio(mensagem.texto).map((parte, i) => (parte.tipo === 'link'
        ? <LinkDoCardapio key={i} url={parte.texto} />
        : parte.texto))}
    </p>
  )
}

// Rodada 12 (#19): o link do convite do cardápio abre a página do cliente
// numa janela do tamanho de um celular, a mesma que o cliente recebe. Antes
// era texto puro e não dava para ver o que o cliente vê a partir da conversa.
function LinkDoCardapio({ url }) {
  const abrir = (evento) => {
    evento.preventDefault()
    window.open(url, 'cdb-cardapio-cliente', 'width=420,height=860')
  }
  return (
    <a className={css.linkCardapio} href={url} target="_blank" rel="noreferrer" title="Abrir como o cliente vê" onClick={abrir}>
      {url}
    </a>
  )
}

export function Balao({ mensagem, trocaDeVoz, aoAbrirDefinicaoAutomatica }) {
  if (mensagem.dir === 'sistema') {
    return (
      <p className={`${css.balao} ${css.sistema} ${trocaDeVoz ? css.trocaDeVoz : ''}`}>
        <Icone nome="check" /> {mensagem.texto}
      </p>
    )
  }
  const entregue = mensagem.status === 'lida'
  return (
    <div className={`${css.balao} ${CLASSE[mensagem.dir]} ${mensagem.automatica ? css.doAutomatico : ''} ${trocaDeVoz ? css.trocaDeVoz : ''}`}>
      <Corpo mensagem={mensagem} />
      <span className={css.meta}>
        {/* Item D (banca 10, pedido 24/09 04h12 "editar ali na hora"): a
            etiqueta que já marcava "automática" vira um botão que abre a
            definição correspondente na biblioteca de Respostas. */}
        {mensagem.automatica && !mensagem.modelo && (
          <button
            type="button"
            className={`${css.selo} ${css.seloBotao}`}
            onClick={() => aoAbrirDefinicaoAutomatica?.(mensagem.regra)}
          >
            <Icone nome="raio" /> automática
          </button>
        )}
        {mensagem.modelo && <span className={css.selo}><Icone nome="modelo" /> modelo</span>}
        {mensagem.programada && <span className={css.selo}><Icone nome="relogio" /> programada</span>}
        <time dateTime={mensagem.em}>{horaCurta(mensagem.em)}</time>
        {mensagem.dir === 'out' && (
          <span>
            <span aria-hidden="true">{entregue ? '✓✓' : '✓'}</span>
            <span className="sr">{entregue ? 'entregue' : 'enviando'}</span>
          </span>
        )}
      </span>
    </div>
  )
}
