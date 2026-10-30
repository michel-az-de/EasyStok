import { mapaDaViagem, tituloDaViagem } from '../../dominio/mapaViagem'
import css from './MapaViagem.module.css'

const CHAVE_EMBED = import.meta.env?.VITE_GOOGLE_MAPS_EMBED_KEY ?? ''

export function MapaDaViagem({ viagem = null, endereco, pedidoId, nomeCliente }) {
  const mapa = mapaDaViagem({ viagem, pedidoId, endereco, chaveEmbed: CHAVE_EMBED })
  const saida = viagem?.saiuEm ? new Date(viagem.saiuEm) : null
  const temSaida = saida && !Number.isNaN(saida.getTime())
  return <section className={css.painel} aria-label={nomeCliente ? `Mapa e viagem de ${nomeCliente}` : 'Mapa da entrega'}>
    <header className={css.cabecalho}>
      <h3>{viagem ? tituloDaViagem(viagem) : 'Mapa da entrega'}</h3>
    </header>
    <div className={`${css.conteudo} ${!mapa.embedUrl ? css.semMapa : ''}`}>
      <div className={css.mapa}>
        {mapa.embedUrl
          ? <iframe title={mapa.roteiro ? 'Mapa do roteiro da viagem' : 'Mapa do destino da entrega'} src={mapa.embedUrl}
            loading="lazy" referrerPolicy="strict-origin-when-cross-origin" allowFullScreen />
          : <p className={css.indisponivel}>{mapa.temEndereco ? 'Mapa indisponível neste ambiente.' : 'Adicione um endereço para ver o mapa.'}</p>}
        {mapa.mapsUrl && <a href={mapa.mapsUrl} target="_blank" rel="noreferrer">{mapa.roteiroExterno ? 'Abrir roteiro no Maps' : 'Abrir destino no Maps'}</a>}
        {viagem && <small>{mapa.roteiro ? 'Roteiro planejado' : 'Destino deste pedido'} · Sem posição em tempo real.</small>}
      </div>
      {viagem && <div className={css.viagem}>
          {nomeCliente && <p>{nomeCliente}</p>}
          {temSaida && <p>Saída às {saida.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</p>}
          <ol className={css.paradas}>{viagem.paradas.map((parada) => <li key={parada.pedidoId} aria-current={parada.pedidoId === pedidoId ? 'step' : undefined}>
            <strong>{parada.numeroDoDia ? `Pedido ${parada.numeroDoDia}` : `Pedido ${parada.numeroPedido}`}{parada.clienteNome && ` · ${parada.clienteNome}`}</strong>
            <span>{parada.endereco || 'Endereço não informado'}</span>
            <small>{parada.entregueEm ? 'Entregue' : 'A entregar'}</small>
          </li>)}</ol>
      </div>}
    </div>
  </section>
}
