import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { ETAPAS_KDS, avisoDeInicio, gruposDoKds, nomeNaComanda, proximoStatusKds } from '../../dominio/kds'
import css from './cozinha.module.css'

// A comanda do cliente aberta da Cozinha (issue #1446): o que o canhoto de papel
// leva, legível no tablet, com a trilha de etapas e o próximo toque. Mesmo
// agrupamento do canhoto (`gruposDoKds`), então a tela e o papel não divergem.
// Os itens não se editam aqui: o pedido já está no EasyStok (F03).
export function ComandaKds({ pedido, agora, linhas, movendo, aoAvancar, aoImprimir, aoReimprimir, aoFechar }) {
  const proximo = proximoStatusKds(pedido.status)
  const aviso = avisoDeInicio(pedido, agora)
  const atual = ETAPAS_KDS.findIndex((e) => e.status === pedido.status)

  return (
    <Modal
      titulo={`Comanda ${pedido.numeroCurto}`}
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao variante="texto" onClick={() => aoReimprimir(pedido.id)}>Reimprimir na fila</Botao>
          <Botao icone="printer" onClick={() => aoImprimir(pedido)}>Imprimir canhoto</Botao>
          {proximo && (
            <Botao variante="primario" disabled={movendo} onClick={() => aoAvancar(pedido.id, proximo.status)}>
              {movendo ? 'Enviando…' : proximo.toque}
            </Botao>
          )}
        </>
      )}
    >
      <div className={css.comanda}>
        <p className={css.comandaCliente}>{nomeNaComanda(pedido)}</p>
        {pedido.endereco && <p className={css.comandaApoio}>{pedido.endereco}</p>}
        <p className={css.comandaApoio}>
          <Icone nome="relogio" tamanho={16} /> {pedido.janela ? `Janela ${pedido.janela.label}` : 'Para já, sem janela'}
          {aviso && <span className={aviso.atrasado ? css.comandaAtraso : undefined}> · {aviso.texto}</span>}
        </p>

        <ol className={css.trilha} aria-label="Etapas do pedido">
          {ETAPAS_KDS.map((etapa, i) => {
            const estado = i < atual ? 'feita' : i === atual ? 'atual' : 'futura'
            return (
              <li key={etapa.status} className={css[`etapa-${estado}`]} aria-current={estado === 'atual' ? 'step' : undefined}>
                <span className={css.marcaEtapa}>{estado === 'feita' ? <Icone nome="check" tamanho={14} /> : i + 1}</span>
                {etapa.rotulo}
              </li>
            )
          })}
        </ol>

        {gruposDoKds(pedido, linhas).map((grupo) => (
          <section key={grupo.chave} className={css.grupo} aria-label={grupo.rotulo}>
            <span className={css.rotuloGrupo}>{grupo.rotulo}</span>
            <ul className={css.itens}>
              {grupo.itens.map((linha) => (
                <li key={linha.sku} className={`${css.item} ${css.itemComanda}`}>
                  <span className={css.qtd}>{linha.qtd}×</span>
                  <span className={css.nomeItem}>{linha.produto.nome}</span>
                  <span className={css.porcao}>{linha.produto.porcao}</span>
                  {linha.obs && <span className={css.obs}>{linha.obs}</span>}
                </li>
              ))}
            </ul>
          </section>
        ))}

        {pedido.observacoes && <p className={css.obsPedido}>{pedido.observacoes}</p>}
        {pedido.requerAprovacao && (
          <p className={css.motivoRecusa}>
            <Icone nome="alerta" tamanho={16} /> {pedido.motivoRequerAprovacao ?? 'Pedido aguarda aprovação da dona.'}
          </p>
        )}
      </div>
    </Modal>
  )
}
