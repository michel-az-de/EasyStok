import { useRelogio } from '../../hooks/useRelogio'
import { horaCurta } from '../../dominio/formato'
import { COLUNAS_KDS, avisoDeInicio, proximoStatusKds } from '../../dominio/kds'
import { useCozinhaApi } from '../../aplicacao/useCozinhaApi'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Vazio } from '../../componentes/Vazio'
import css from './cozinha.module.css'

// Cozinha no modo API (F05, issue #1218): a fila real do KDS sobre Pedido (S19),
// ao vivo pelo SSE de operação (S18), com início previsto e atraso (S21) e o
// canhoto da S20. Um toque no cartão chama a API; a máquina de estados é dela.
// A cozinha da demonstração (`TelaCozinha`, espelho do Balcão) segue intacta.
// Mesmo desenho do `INICIO_DO_RELOGIO` do App no modo API: relógio real, relido a cada tique (F07).
const ABERTURA = Date.now()

export function TelaCozinhaApi() {
  const agora = useRelogio(ABERTURA, undefined, { real: true })
  const { pedidos, erro, aoVivo, movendo, avancar, imprimirCanhoto, reimprimir, limparErro } = useCozinhaApi()

  return (
    <div className={css.pagina}>
      <header className={css.topoPagina}>
        <h1>Cozinha</h1>
        <span className={css.horaTopo}>{aoVivo ? 'Ao vivo' : 'Atualizando a cada 15 s'}</span>
        <span className={css.horaTopo}>{horaCurta(new Date(agora).toISOString())}</span>
      </header>
      {erro && (
        <p className={css.motivoRecusa} role="alert">
          <Icone nome="circle-x" tamanho={16} /> {erro}
          <Botao variante="texto" onClick={limparErro}>Fechar</Botao>
        </p>
      )}
      {pedidos === null
        ? <p className={css.aviso}>Carregando a fila da cozinha…</p>
        : (
          <div className={css.conteudo}>
            <div className={css.colunas}>
              {COLUNAS_KDS.map((coluna) => {
                const cartoes = pedidos.filter((p) => p.status === coluna.status)
                return (
                  <section key={coluna.status} className={css.coluna} aria-label={coluna.rotulo}>
                    <h2>{coluna.rotulo} <span className={css.contagem}>{cartoes.length}</span></h2>
                    <ul className={css.listaColuna}>
                      {cartoes.length === 0 && (
                        <li><Vazio titulo="Nenhum pedido">Os cartões aparecem aqui conforme o pedido avança.</Vazio></li>
                      )}
                      {cartoes.map((p) => (
                        <CartaoKds
                          key={p.id} pedido={p} agora={agora} movendo={movendo.has(p.id)}
                          aoAvancar={avancar} aoImprimir={imprimirCanhoto} aoReimprimir={reimprimir}
                        />
                      ))}
                    </ul>
                  </section>
                )
              })}
            </div>
          </div>
        )}
    </div>
  )
}

function CartaoKds({ pedido, agora, movendo, aoAvancar, aoImprimir, aoReimprimir }) {
  const proximo = proximoStatusKds(pedido.status)
  const aviso = avisoDeInicio(pedido, agora)
  const cor = aviso?.atrasado ? 'cor-atraso' : pedido.status === 'aguardando' ? 'cor-esperando' : 'cor-preparo'
  const cliente = [pedido.clienteNome, pedido.clienteApt].filter(Boolean).join(' · ')

  return (
    <li className={`${css.cartao} ${css[cor]}`}>
      <div className={css.cabecaCartao}>
        <span className={css.numeroCartao}>{pedido.numeroCurto}{cliente && ` · ${cliente}`}</span>
      </div>
      <p className={css.rotuloSituacao}>{aviso ? aviso.texto : pedido.statusRotulo}</p>
      {pedido.janela && (
        <p className={css.horaJanela}>
          <Icone nome="relogio" tamanho={16} /> {pedido.janela.label}
        </p>
      )}
      <ul className={css.itens}>
        {pedido.itens.map((item, i) => (
          <li key={i} className={css.item}>
            <span className={css.qtd}>{item.qtd}×</span>
            <span className={css.nomeItem}>{item.nome}{item.variacao && ` (${item.variacao})`}</span>
            <span>{item.molho}</span>
            {item.observacao && <span className={css.obs}>{item.observacao}</span>}
          </li>
        ))}
      </ul>
      {pedido.observacoes && <p className={css.motivoDesabilitado}>{pedido.observacoes}</p>}
      {proximo && (
        <Botao
          largo variante="primario" className={css.botaoAvancar} disabled={movendo}
          onClick={() => aoAvancar(pedido.id, proximo.status)}
        >
          {movendo ? 'Enviando…' : proximo.toque}
        </Botao>
      )}
      <div className={css.cabecaCartao}>
        <Botao variante="texto" icone="printer" onClick={() => aoImprimir(pedido.id)}>Canhoto</Botao>
        <Botao variante="texto" onClick={() => aoReimprimir(pedido.id)}>Reimprimir na fila</Botao>
      </div>
    </li>
  )
}
