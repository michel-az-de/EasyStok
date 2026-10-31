import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Pilula } from '../../componentes/Pilula'
import { CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { CampoReais } from '../../componentes/CampoReais'
import { useCorridasDaViagem } from '../../aplicacao/useCorridasDaViagem'
import {
  EMPRESAS_DE_CORRIDA, GORJETAS_CENTAVOS, VEICULOS_CORRIDA, corpoCorrida, corridaDaApi, custoDasCorridas,
  empresaPadraoDaCorrida, erroDoFormularioCorrida, formularioDaCorrida, motivoSemChamada, motoristaDaCorrida,
  podeChamarLalamove, resumoDaCorrida, resumoDaCotacao, statusDaCorrida,
} from '../../dominio/entregasApi'
import { centavosDoTexto, lerMoeda, moeda, textoDosCentavos } from '../../dominio/formato'
import css from './entregasApi.module.css'

// Corrida pedida por fora (I9.2, #1691): a atendente chama a 99 ou a Uber pelo app e anota aqui a
// empresa, o custo e o motorista. Três toques: abrir, digitar o custo, salvar. A Lalamove, com a loja
// conectada, é chamada daqui pela API (#2162): cotar, chamar, e o status passa a vir sozinho.
export function CorridasDaViagem({ viagem, entregadores, ocupado }) {
  const { corridas, conectada, erro, enviando, registrar, corrigir, cotar, chamar, cancelar, gorjeta } = useCorridasDaViagem(viagem.id)
  // null = fechado; { id: null, ... } = corrida nova; { id, ... } = corrigindo.
  const [form, setForm] = useState(null)
  const [aviso, setAviso] = useState(null)
  const travado = ocupado || enviando
  const campo = (k) => ({ value: form[k], disabled: travado, onChange: (e) => { setForm({ ...form, [k]: e.target.value }); setAviso(null) } })

  const abrirNova = () => { setAviso(null); setForm({ id: null, empresa: empresaPadraoDaCorrida(viagem, entregadores), custo: '', motorista: '' }) }
  const abrirCorrecao = (corrida) => { setAviso(null); setForm({ id: corrida.id, ...formularioDaCorrida(corrida) }) }
  const salvar = (e) => {
    e.preventDefault()
    const motivo = erroDoFormularioCorrida(form)
    if (motivo) { setAviso(motivo); return }
    const corpo = corpoCorrida(form)
    const envio = form.id
      ? corrigir(form.id, { custoCentavos: corpo.custoCentavos, motorista: corpo.motorista })
      : registrar(corpo)
    envio.then((ok) => { if (ok) setForm(null) })
  }

  const total = custoDasCorridas(corridas)
  return (
    <section className={css.secao} aria-label="Corridas desta viagem">
      {(corridas ?? []).length > 0 && (
        <ul className={css.lista}>
          {corridas.map((corrida) => corridaDaApi(corrida) ? (
            <CorridaPelaApi key={corrida.id} corrida={corrida} travado={travado} cancelar={cancelar} gorjeta={gorjeta} />
          ) : (
            <li key={corrida.id} className={`${css.cartao} ${css.linha}`}>
              <span className={css.cresce}>{resumoDaCorrida(corrida)}</span>
              <Botao variante="texto" disabled={travado} aria-label={`Corrigir corrida ${resumoDaCorrida(corrida)}`} onClick={() => abrirCorrecao(corrida)}>Corrigir</Botao>
            </li>
          ))}
        </ul>
      )}
      {(corridas ?? []).length > 1 && <p className={css.apoio}>Custo das corridas desta viagem: <strong>{moeda(total / 100)}</strong></p>}
      {erro && <p className={css.erroCampo} role="alert">{erro}</p>}

      {!form && corridas !== null && (podeChamarLalamove(viagem, corridas, conectada)
        ? <ChamadaLalamove travado={travado} cotar={cotar} chamar={chamar} />
        : motivoSemChamada(viagem, corridas, conectada) && <p className={css.apoio}>{motivoSemChamada(viagem, corridas, conectada)}</p>)}

      {form ? (
        <form className={css.formulario} onSubmit={salvar} noValidate>
          <CampoSelecao rotulo="Empresa" opcoes={EMPRESAS_DE_CORRIDA} {...campo('empresa')} disabled={travado || Boolean(form.id)} />
          <CampoReais rotulo="Custo (R$)" autoFocus centavos={centavosDoTexto(form.custo)} ler={lerMoeda} mostrarZero disabled={travado}
            aoMudar={(centavos) => { setForm({ ...form, custo: textoDosCentavos(centavos) }); setAviso(null) }} />
          <CampoTexto rotulo="Motorista (opcional)" maxLength={120} {...campo('motorista')} />
          <div className={css.linha}>
            <Botao tipo="submit" variante="primario" disabled={travado}>{form.id ? 'Salvar correção' : 'Salvar corrida'}</Botao>
            <Botao variante="texto" disabled={travado} onClick={() => { setForm(null); setAviso(null) }}>Cancelar</Botao>
          </div>
          {aviso && <p className={css.erroCampo} role="alert">{aviso}</p>}
        </form>
      ) : (
        <div className={css.linha}>
          <Botao variante="secundario" icone="plus" disabled={travado || corridas === null} onClick={abrirNova}>Registrar corrida</Botao>
          {corridas !== null && corridas.length === 0 && <span className={css.apoio}>Chamou 99 ou Uber pelo app? Anote o custo aqui.</span>}
        </div>
      )}
    </section>
  )
}

// Cotar e chamar: a cotação mostra preço e distância, e a chamada usa a cotação relida no servidor (o preço
// nunca sai daqui). A cotação vale 5 min; vencida, a API pede para cotar de novo.
function ChamadaLalamove({ travado, cotar, chamar }) {
  const [veiculo, setVeiculo] = useState('Moto')
  const [cotacao, setCotacao] = useState(null)
  const [observacao, setObservacao] = useState('')
  const cotarAgora = () => cotar(veiculo).then((c) => { if (c && c !== true) setCotacao(c) })
  const chamarAgora = () => chamar({ cotacaoId: cotacao.cotacaoId, veiculo, observacao: observacao.trim() || null })
    .then((ok) => { if (ok) setCotacao(null) })
  if (!cotacao) {
    return (
      <div className={css.linha}>
        <CampoSelecao rotulo="Veículo" opcoes={VEICULOS_CORRIDA} value={veiculo} disabled={travado} onChange={(e) => setVeiculo(e.target.value)} />
        <Botao variante="primario" icone="moto" disabled={travado} onClick={cotarAgora}>Cotar Lalamove</Botao>
      </div>
    )
  }
  return (
    <div className={css.formulario} aria-label="Cotação da Lalamove">
      <p><strong>Lalamove de {veiculo.toLowerCase()}: {resumoDaCotacao(cotacao)}</strong></p>
      <p className={css.apoio}>Vale até {new Date(cotacao.expiraEm).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}. A coleta é na cozinha e a entrega no endereço do pedido.</p>
      <CampoTexto rotulo="Recado para o motorista (opcional)" maxLength={120} value={observacao} disabled={travado} onChange={(e) => setObservacao(e.target.value)} />
      <div className={css.linha}>
        <Botao variante="primario" disabled={travado} onClick={chamarAgora}>Chamar por {moeda(cotacao.valorCentavos / 100)}</Botao>
        <Botao variante="texto" disabled={travado} onClick={() => setCotacao(null)}>Desistir</Botao>
      </div>
    </div>
  )
}

// A corrida chamada pela API: status do provedor, motorista e o que a Lalamove ainda aceita (cancelar só sem
// custo, gorjeta só procurando motorista). Atualiza sozinha a cada 15 s enquanto está em curso.
function CorridaPelaApi({ corrida, travado, cancelar, gorjeta }) {
  const status = statusDaCorrida(corrida)
  const motorista = motoristaDaCorrida(corrida)
  return (
    <li className={css.cartao} aria-label={`Corrida Lalamove: ${status.rotulo}`}>
      <div className={css.linha}>
        <Pilula tom={status.tom}>{status.rotulo}</Pilula>
        <span className={css.cresce}>Lalamove · {moeda(corrida.custoCentavos / 100)}{corrida.veiculo === 'Carro' ? ' · carro' : ''}</span>
        {corrida.linkRastreio && <a href={corrida.linkRastreio} target="_blank" rel="noopener noreferrer"
          aria-label="Acompanhar no mapa da Lalamove (abre em nova aba)">Acompanhar ↗</a>}
      </div>
      {motorista && (
        <p className={css.apoio}>
          Motorista: {motorista}
          {corrida.motoristaTelefone && <> · <a href={`tel:${corrida.motoristaTelefone}`}>{corrida.motoristaTelefone}</a></>}
        </p>
      )}
      {corrida.ativa && (corrida.podeCancelar || corrida.status === 'ProcurandoMotorista') && (
        <div className={css.linha}>
          {corrida.status === 'ProcurandoMotorista' && GORJETAS_CENTAVOS.map((centavos) => (
            <Botao key={centavos} variante="texto" disabled={travado} aria-label={`Dar gorjeta de ${moeda(centavos / 100)} para achar motorista`}
              onClick={() => gorjeta(corrida.id, centavos)}>+ {moeda(centavos / 100)}</Botao>
          ))}
          {corrida.podeCancelar && <Botao variante="texto" disabled={travado} onClick={() => cancelar(corrida.id)}>Cancelar corrida</Botao>}
        </div>
      )}
    </li>
  )
}
