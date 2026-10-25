import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { ImprimirPedido } from '../../componentes/ImprimirPedido'
import { DesfazerComPrazo } from '../../componentes/DesfazerComPrazo'
import { Modal } from '../../componentes/Modal'
import { impressaoDoPedido } from '../../aplicacao/impressos'
import {
  ETAPAS_KDS, avisoDeInicio, colunaDoStatusKds, gruposDoKds, nomeNaComanda, proximoStatusKds, rotuloDoDestinoKds,
} from '../../dominio/kds'
import { nomeDoPedido } from '../../dominio/pedido'
import css from './cozinha.module.css'

// A comanda do cliente aberta da Cozinha (issue #1446): o que o canhoto de papel
// leva, legível no tablet, com a trilha de etapas e o próximo toque. Mesmo
// agrupamento do canhoto (`gruposDoKds`), então a tela e o papel não divergem.
// Os itens não se editam aqui: o pedido já está no EasyStok (F03).
// #1584: o erro da tela (`erro`) aparece aqui dentro; desenhado só na página, ficava inerte
// atrás do modal e "Marcar pronto" parecia não ter feito nada.
export function ComandaKds({
  pedido, agora, linhas, movendo, erro, aoAvancar, aoReimprimir, aoLimparErro, aoFechar, adiado = null, aoDesfazer = null,
}) {
  const proximo = proximoStatusKds(pedido.status)
  const aviso = avisoDeInicio(pedido, agora)
  const atual = ETAPAS_KDS.findIndex((e) => e.status === colunaDoStatusKds(pedido.status))
  // #1584: "Reimprimir na fila" não dava retorno. A ação guarda a própria falha no erro da
  // tela, então o erro antigo sai antes e a confirmação só vale se nenhum erro voltar.
  const [reimpressao, setReimpressao] = useState(null) // null | 'enviando' | 'feita'
  const reimprimir = async () => {
    aoLimparErro?.()
    setReimpressao('enviando')
    await aoReimprimir(pedido.id)
    setReimpressao('feita')
  }

  return (
    <Modal
      titulo={nomeDoPedido(pedido).frase}
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao variante="texto" disabled={reimpressao === 'enviando'} onClick={reimprimir}>
            {reimpressao === 'enviando' ? 'Reenviando…' : 'Reimprimir na fila'}
          </Botao>
          {/* #1839: um caminho só, o impresso da API com o papel na tela (o PDF próprio saiu). */}
          <ImprimirPedido {...impressaoDoPedido(pedido.id, null, nomeNaComanda(pedido))} documentoInicial="cozinha" rotulo="Imprimir canhoto" />
          {/* #1686 (item 5): o mesmo "Desfazer" do cartão. */}
          {proximo && adiado && (
            <DesfazerComPrazo ate={adiado.ate} texto={`Vai para ${rotuloDoDestinoKds(adiado.status)}`} aoDesfazer={() => aoDesfazer?.(pedido.id)} />
          )}
          {proximo && !adiado && (
            <Botao variante="primario" disabled={movendo} onClick={() => aoAvancar(pedido.id, proximo.status)}>
              {movendo ? 'Enviando…' : proximo.toque}
            </Botao>
          )}
        </>
      )}
    >
      <div className={css.comanda}>
        {erro && (
          <p className={css.motivoRecusa} role="alert">
            <Icone nome="circle-x" tamanho={16} /> {erro}
          </p>
        )}
        {reimpressao === 'feita' && !erro && (
          <output className={css.comandaApoio}>
            <Icone nome="check" tamanho={16} /> Canhoto de volta na fila de impressão.
          </output>
        )}
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
