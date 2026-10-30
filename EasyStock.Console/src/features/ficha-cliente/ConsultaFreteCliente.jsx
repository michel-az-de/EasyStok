import { useEffect, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { consultarFreteDoCliente } from '../../aplicacao/freteDoCliente'
import { useAtendimento } from '../../aplicacao/contextos'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { useViagemDoCliente } from '../../aplicacao/useViagemDoCliente'
import { moeda } from '../../dominio/formato'
import { MapaDaViagem } from './MapaViagem'
import css from './ModalCliente.module.css'

export function ConsultaFreteCliente({ conversa, pedidoId = conversa.pedido?.pedidoId, aoFechar, aoEditarEndereco, aoVerAoLado }) {
  const { fonteApi } = useAtendimento()
  const { permite } = useAcessoModulos()
  const [tentativa, setTentativa] = useState(0)
  const [estado, setEstado] = useState({})
  const { clienteId, cliente } = conversa
  const chaveFrete = `${clienteId}:${cliente.endereco}:${tentativa}`
  const frete = estado.chave === chaveFrete ? estado : { carregando: true }
  const podeVerViagem = Boolean(fonteApi && clienteId && pedidoId && permite('atendimento') && permite('entregas'))
  const consulta = useViagemDoCliente({ clienteId, pedidoId, ativo: podeVerViagem })
  useEffect(() => {
    const controle = new AbortController()
    let ativo = true
    consultarFreteDoCliente(clienteId, controle.signal).then((dados) => {
      if (!Number.isFinite(dados?.valor) || dados.valor < 0) throw new Error('A consulta não retornou um valor de frete. Tente de novo.')
      if (ativo) setEstado({ chave: chaveFrete, dados })
    }).catch((erro) => {
      if (ativo && !controle.signal.aborted) setEstado({ chave: chaveFrete, erro: erro.message, combinar: erro.status === 422 })
    })
    return () => { ativo = false; controle.abort() }
  }, [clienteId, cliente.endereco, tentativa, chaveFrete])

  return <Modal titulo="Calcular frete" aoFechar={aoFechar} largura="900px"
    rodape={<><Botao variante="texto" onClick={aoEditarEndereco}>Editar endereço</Botao><Botao onClick={aoFechar}>Fechar</Botao></>}>
    <div className={css.consultaFrete}>
      <p>{conversa.nome}</p>
      <p className={css.enderecoFrete}>{cliente.endereco}</p>
      {frete.carregando && <output>Calculando frete…</output>}
      {frete.dados && <output className={css.valorFrete}>
        <strong>{frete.dados.valorFormatado || moeda(frete.dados.valor / 100)}</strong>
        {frete.dados.etaLabel && <span>{frete.dados.etaLabel}</span>}
        {frete.dados.zonaLabel && <small>{frete.dados.zonaLabel}</small>}
      </output>}
      {frete.erro && <div role={frete.combinar ? 'status' : 'alert'}>
        {frete.combinar && <h3>Frete a combinar</h3>}
        <p>{frete.erro}</p>
        {frete.combinar ? <p>Combine o frete com o cliente antes de cobrar.</p>
          : <Botao onClick={() => setTentativa((v) => v + 1)}>Tentar de novo</Botao>}
      </div>}
    </div>
    {consulta.carregando && <output>Consultando a viagem…</output>}
    {consulta.erro && <div role="alert"><p>{consulta.erro}</p><Botao onClick={consulta.atualizar}>Tentar consultar viagem</Botao></div>}
    {consulta.lida && !consulta.viagem && <p>Não há viagem ativa vinculada a este pedido.</p>}
    <MapaDaViagem viagem={consulta.viagem} endereco={cliente.endereco} pedidoId={pedidoId} nomeCliente={conversa.nome} />
    {consulta.viagem?.situacao === 'EmRota' && aoVerAoLado && <Botao onClick={() => aoVerAoLado({ viagem: consulta.viagem,
      endereco: cliente.endereco, clienteId, pedidoId, nomeCliente: conversa.nome })}>Ver ao lado</Botao>}
  </Modal>
}
