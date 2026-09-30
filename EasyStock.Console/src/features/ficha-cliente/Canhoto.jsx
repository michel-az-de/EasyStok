import { AcoesPdf } from '../../componentes/AcoesPdf'
import { Botao } from '../../componentes/Botao'
import { FolhaImpressao } from '../../componentes/FolhaImpressao'
import { Modal } from '../../componentes/Modal'
import { pdfDoCanhoto } from '../../dominio/impressao'
import { agruparPorLinha, numeroCurto } from '../../dominio/pedido'
import { Cabecalho } from './Cabecalho'
import css from './comanda.module.css'

// O papel do canhoto (RN-29, RN-33): mesmo desenho da comanda, sem os
// controles de edição e sem preço. Preço não entra: quem cozinha não precisa
// de valor para produzir, e o documento não é cupom fiscal (RN-28, as 3
// perguntas do método). Usado duas vezes por `ModalCanhoto` — uma visível na
// tela, dentro da Modal, e uma escondida dentro da FolhaImpressao, que só a
// impressão revela (`estilos/base.css`). Exportado porque `FilaCanhotos.jsx`
// (RN-27, decisão 31 item 4) também precisa dele: a fila automática virou
// não bloqueante e imprime direto da própria gaveta, sem passar pela
// `ModalCanhoto` — mas o portal de impressão ainda precisa do mesmo papel.
export function PapelCanhoto({
  pedido, itens, linhas, nomeCliente, endereco, faixa,
}) {
  const grupos = agruparPorLinha(itens, linhas)
  return (
    <div className={css.papelCanhoto}>
      <p className={css.canhotoAviso}>Canhoto de pedido, não é cupom fiscal</p>
      <Cabecalho
        numero={pedido.numero}
        nomeCliente={nomeCliente}
        faixa={faixa}
        enviadaEm={pedido.cobranca?.criadaEm ?? null}
      />
      {endereco && <p className={css.subCabecalho}>{endereco}</p>}
      <hr className={css.separador} />
      {grupos.map((grupo, indice) => (
        <div key={grupo.chave} className={css.grupo}>
          <span className={css.rotuloGrupo}>{grupo.rotulo}</span>
          <ul className={css.itens}>
            {grupo.itens.map((linha) => (
              <li className={css.item} key={linha.sku}>
                <div className={css.linhaItem}>
                  <span className={css.qtd}>{linha.qtd}×</span>
                  <span className={css.corpo}>
                    <span className={css.nomeItem}>{linha.produto?.nome}</span>
                    <span className={css.porcao}>{linha.produto?.porcao}</span>
                  </span>
                </div>
                {linha.obs && <span className={css.obs}>{linha.obs}</span>}
              </li>
            ))}
          </ul>
          {indice < grupos.length - 1 && <hr className={css.separador} />}
        </div>
      ))}
    </div>
  )
}

// Casca de impressão do canhoto (direção 19, seção 3.4): "Ver canhoto" abre
// isto numa Modal de verdade (`Modal.jsx`, trap de foco e fundo inerte do
// próprio navegador), nunca mais cobrindo a tela inteira. A mesma casca serve
// o clique manual (BlocoPedido) e a fila automática do pagamento
// (FilaCanhotos, RN-27) — as duas pontas do canhoto.
export function ModalCanhoto({
  pedido, itens, linhas, nomeCliente, endereco, faixa, aoFechar, aoImprimir,
}) {
  // Rodada 11 (issue #9): a Modal é a pré-visualização; "Baixar PDF" e
  // "Imprimir" usam o mesmo PDF de 80 mm (dominio/impressao.js). O ponto onde
  // a impressora térmica real entra depois continua sendo o Imprimir;
  // `aoImprimir` é o gancho que marca o canhoto como impresso (RN-27).
  const gerar = () => pdfDoCanhoto({ pedido, itens, linhas, nomeCliente, endereco, faixa })
  return (
    <>
      <Modal
        titulo={`Canhoto ${numeroCurto(pedido.numero)}`}
        aoFechar={aoFechar}
        rodape={(
          <>
            <Botao variante="texto" onClick={aoFechar}>Fechar</Botao>
            <AcoesPdf gerar={gerar} aoImprimir={aoImprimir} planoB={() => window.print()} />
          </>
        )}
      >
        <div className={css.canhotoTela}>
          <div className={css.comanda}>
            <PapelCanhoto
              pedido={pedido} itens={itens} linhas={linhas}
              nomeCliente={nomeCliente} endereco={endereco} faixa={faixa}
            />
          </div>
        </div>
      </Modal>
      <FolhaImpressao>
        <PapelCanhoto
          pedido={pedido} itens={itens} linhas={linhas}
          nomeCliente={nomeCliente} endereco={endereco} faixa={faixa}
        />
      </FolhaImpressao>
    </>
  )
}
