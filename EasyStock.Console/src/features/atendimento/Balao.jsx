import { memo, useEffect, useRef, useState } from 'react'
import { Icone } from '../../componentes/Icone'
import { mesmasProps } from '../../aplicacao/igualdade'
import { horaCurta } from '../../dominio/formato'
import { partesComLinkDoCardapio } from '../../dominio/cardapioLink'
import { falhaDeEnvio, TIPOS_DE_FALHA } from '../../dominio/falhaDeEnvio'
import { tiqueDaMensagem } from '../../dominio/mensagem'
import { BolhaArquivo, BolhaAudio, BolhaPeca } from './BolhaAnexo'
import { BolhaAvaliacaoPedido, BolhaAvaliacaoResposta } from './BolhaAvaliacao'
import { BolhaMidiaApi } from './BolhaMidiaApi'
import css from './atendimento.module.css'

const CLASSE = { in: css.entrada, out: css.saida }

function Corpo({ mensagem }) {
  // Modo API: arquivo do canal servido pelo endpoint autenticado (#1287).
  if (mensagem.midia) return <BolhaMidiaApi mensagem={mensagem} />
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
      {/* #1510: mensagem que chega do cliente nunca vira link; a nossa, só da própria origem. */}
      {partesComLinkDoCardapio(mensagem.texto, mensagem.dir === 'in' ? null : window.location.origin).map((parte, i) => (parte.tipo === 'link'
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
    window.open(url, 'cdb-cardapio-cliente', 'width=420,height=860,noopener')
  }
  return (
    <a className={css.linkCardapio} href={url} target="_blank" rel="noopener noreferrer" title="Abrir como o cliente vê" onClick={abrir}>
      {url}
    </a>
  )
}

// #1577: em `memo`. Mensagem nova na conversa não re-renderiza os balões antigos (até 100, cada um com a
// regex de link). A mensagem compara por conteúdo: a leitura da API a devolve como objeto novo sem ter mudado.
export const Balao = memo(function Balao({ mensagem, trocaDeVoz, aoAbrirDefinicaoAutomatica, aoReenviar, aoAbrirModelos }) {
  // #1761: o Reenviar não dava retorno nenhum. O botão diz "Reenviando…", trava o segundo toque
  // e, se o canal recusar de novo, a bolha diz que a nova tentativa também não saiu.
  const [reenvio, setReenvio] = useState(null)
  const reenviando = useRef(false)
  // O resultado nasce abaixo do botão, na última bolha do fio: traz para a vista.
  const resultadoDoReenvio = useRef(null)
  useEffect(() => {
    if (reenvio === 'falhou') resultadoDoReenvio.current?.scrollIntoView({ block: 'nearest' })
  }, [reenvio])
  async function reenviar() {
    if (reenviando.current) return
    reenviando.current = true
    setReenvio('indo')
    let saiu = false
    try {
      const resposta = await aoReenviar(mensagem.id)
      saiu = Boolean(resposta) && resposta.status !== 'falhou'
    } catch {
      saiu = false
    } finally {
      reenviando.current = false
    }
    setReenvio(saiu ? null : 'falhou')
  }

  if (mensagem.dir === 'sistema') {
    return (
      <p className={`${css.balao} ${css.sistema} ${trocaDeVoz ? css.trocaDeVoz : ''}`}>
        <Icone nome="check" /> {mensagem.texto}
      </p>
    )
  }
  // #1761: entregue e lida deixaram de ser o mesmo "✓✓ entregue" (`tiqueDaMensagem`).
  const tique = mensagem.dir === 'out' ? tiqueDaMensagem(mensagem.status) : null
  // #1761: o erro cru do canal vira frase de balcão; fora da janela ganha o caminho do modelo.
  const falha = mensagem.status === 'falhou' ? falhaDeEnvio(mensagem.erro) : null
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
            title={mensagem.origemAutomatica === 'agente' ? 'Escrita pelo agente do EasyStok' : 'Mensagem automática do sistema'}
            onClick={() => aoAbrirDefinicaoAutomatica?.(mensagem.regra)}
          >
            <Icone nome="raio" /> automática
          </button>
        )}
        {mensagem.modelo && <span className={css.selo} title={mensagem.modeloNome}><Icone nome="modelo" /> modelo</span>}
        {mensagem.programada && <span className={css.selo}><Icone nome="relogio" /> programada</span>}
        <time dateTime={mensagem.em}>{horaCurta(mensagem.em)}</time>
        {mensagem.status === 'falhou' && (
          <span className={css.falhou} role="alert"><Icone nome="alerta" tamanho={14} /> {falha.texto}</span>
        )}
        {falha?.tipo === TIPOS_DE_FALHA.FORA_DA_JANELA && mensagem.dir === 'out' && aoAbrirModelos && (
          <button type="button" className={`${css.selo} ${css.seloBotao} ${css.acaoDaBolha}`} aria-haspopup="dialog" onClick={aoAbrirModelos}>
            <Icone nome="modelo" /> Modelos aprovados
          </button>
        )}
        {/* S57: texto que falhou volta a sair pelo mesmo canal. Só com a ação ligada (modo API). */}
        {mensagem.porSms && <span className={css.selo}><Icone nome="check" /> saiu por SMS</span>}
        {mensagem.status === 'falhou' && mensagem.dir === 'out' && aoReenviar && !mensagem.modelo && !mensagem.semIdServidor && !mensagem.midia && !mensagem.aguardaCliente && (
          <button type="button" className={`${css.selo} ${css.seloBotao} ${css.acaoDaBolha}`} disabled={reenvio === 'indo'} onClick={reenviar}>
            <Icone nome="raio" /> {reenvio === 'indo' ? 'Reenviando…' : 'Reenviar'}
          </button>
        )}
        {mensagem.status === 'falhou' && reenvio === 'falhou' && (
          <output ref={resultadoDoReenvio} className={css.falhou}>Tentei de novo e não saiu.</output>
        )}
        {mensagem.dir === 'out' && mensagem.status === 'enviando' && (
          <span className={css.enviando}>{mensagem.modelo ? 'envio sem confirmação' : 'enviando'}</span>
        )}
        {tique && (
          <span className={tique.lida ? css.tiqueLida : undefined} title={`Mensagem ${tique.rotulo}`}>
            <span aria-hidden="true">{tique.marca}</span>
            <span className={tique.lida ? undefined : 'sr'}> {tique.rotulo}</span>
          </span>
        )}
      </span>
    </div>
  )
}, (antes, depois) => mesmasProps(antes, depois, ['mensagem']))
