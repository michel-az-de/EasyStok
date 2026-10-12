import { AcoesPdf } from '../../componentes/AcoesPdf'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { horaCurta } from '../../dominio/formato'
import { pdfDaRota } from '../../dominio/impressao'
import { marcosDaViagem, urlsDeRota } from '../../dominio/viagem'
import css from './dashboard.module.css'

// "Gerar rota" (seção 6). Integração da rodada 5: quem abre a modal já
// aplicou a ordem sugerida (`ordemSugeridaDaViagem`, dominio/viagem.js) na
// viagem, então a lista aqui é a ordem de verdade, a mesma das paradas e dos
// marcos. `aoDesfazer` só existe quando a ordem mudou, e devolve a anterior.
//
// Rodada 11 (issue #9, pedido do dono "rota em A4"): a lista desta modal é a
// pré-visualização da folha; "Baixar PDF" e "Imprimir" saem do mesmo PDF A4
// (dominio/impressao.js), com a ordem, a chegada prevista e os itens de cada
// parada para quem leva conferir as sacolas.
export function ModalGerarRota({ viagem, janelas, agora, constantes, enderecoDaCasa, cardapio, aoDesfazer, aoFechar }) {
  const ordenadas = viagem.paradas
  const paradasComFaixa = ordenadas.map((c) => ({
    id: c.id, faixa: janelas.find((j) => j.id === c.pedido.janela)?.faixa,
  }))
  const marcos = marcosDaViagem(paradasComFaixa, agora, constantes)
  const enderecos = ordenadas.map((c) => c.cliente?.endereco).filter(Boolean)
  const urls = urlsDeRota(enderecoDaCasa, enderecos)

  return (
    <Modal
      titulo="Rota da viagem"
      descricao={aoDesfazer
        ? 'Paradas reordenadas por início de janela, depois por bairro. Sem mapa embutido: o link abre no Google Maps.'
        : 'A viagem já estava na ordem por início de janela e bairro. Sem mapa embutido: o link abre no Google Maps.'}
      aoFechar={aoFechar}
      rodape={(
        <>
          {aoDesfazer && <Botao variante="texto" onClick={aoDesfazer}>Desfazer ordem</Botao>}
          <Botao variante="texto" onClick={aoFechar}>Fechar</Botao>
          <AcoesPdf gerar={() => pdfDaRota({ viagem, janelas, agora, constantes, enderecoDaCasa, cardapio })} />
        </>
      )}
    >
      <div className={css.acoesViagem}>
        {urls.map((url, indice) => (
          <a key={url} className={css.linkMapa} href={url} target="_blank" rel="noopener noreferrer">
            <Icone nome="external-link" /> Abrir rota no mapa{urls.length > 1 ? ` (parte ${indice + 1})` : ''}
          </a>
        ))}
      </div>
      <ol className={css.listaRota}>
        {ordenadas.map((conversa, indice) => {
          const parada = marcos.porParada.find((p) => p.id === conversa.id)
          return (
            <li key={conversa.id} className={css.paradaRota}>
              <strong>{indice + 1}. {conversa.nome}</strong>
              <span>{conversa.cliente?.endereco}</span>
              <span>
                Chegada prevista {parada?.chegadaReal != null ? horaCurta(new Date(parada.chegadaReal).toISOString()) : '—'}
              </span>
              {conversa.cliente?.endereco && (
                <a
                  className={css.linkMapa}
                  href={'https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(conversa.cliente.endereco)}
                  target="_blank" rel="noopener noreferrer"
                >
                  Abrir no mapa
                </a>
              )}
            </li>
          )
        })}
      </ol>
    </Modal>
  )
}
