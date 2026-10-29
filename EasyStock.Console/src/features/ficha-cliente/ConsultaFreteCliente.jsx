import { useEffect, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { consultarFreteDoCliente } from '../../aplicacao/freteDoCliente'
import { moeda } from '../../dominio/formato'
import css from './ModalCliente.module.css'

export function ConsultaFreteCliente({ conversa, aoFechar, aoEditarEndereco }) {
  const [tentativa, setTentativa] = useState(0)
  const [estado, setEstado] = useState({ carregando: true })
  const { clienteId, cliente } = conversa
  useEffect(() => {
    const controle = new AbortController()
    let ativo = true
    setEstado({ carregando: true })
    consultarFreteDoCliente(clienteId, controle.signal).then((dados) => {
      if (!Number.isFinite(dados?.valor) || dados.valor < 0) throw new Error('A consulta não retornou um valor de frete. Tente de novo.')
      if (ativo) setEstado({ dados })
    }).catch((erro) => {
      if (ativo && !controle.signal.aborted) setEstado({ erro: erro.message, combinar: erro.status === 422 })
    })
    return () => { ativo = false; controle.abort() }
  }, [clienteId, cliente.endereco, tentativa])

  return <Modal titulo="Calcular frete" aoFechar={aoFechar} largura="540px"
    rodape={<><Botao variante="texto" onClick={aoEditarEndereco}>Editar endereço</Botao><Botao onClick={aoFechar}>Fechar</Botao></>}>
    <div className={css.consultaFrete}>
      <p>{conversa.nome}</p>
      <p className={css.enderecoFrete}>{cliente.endereco}</p>
      {estado.carregando && <output role="status">Calculando frete…</output>}
      {estado.dados && <div className={css.valorFrete} role="status">
        <strong>{estado.dados.valorFormatado || moeda(estado.dados.valor / 100)}</strong>
        {estado.dados.etaLabel && <span>{estado.dados.etaLabel}</span>}
        {estado.dados.zonaLabel && <small>{estado.dados.zonaLabel}</small>}
      </div>}
      {estado.erro && <div role={estado.combinar ? 'status' : 'alert'}>
        {estado.combinar && <h3>Frete a combinar</h3>}
        <p>{estado.erro}</p>
        {estado.combinar ? <p>Combine o frete com o cliente antes de cobrar.</p>
          : <Botao onClick={() => setTentativa((v) => v + 1)}>Tentar de novo</Botao>}
      </div>}
    </div>
  </Modal>
}
