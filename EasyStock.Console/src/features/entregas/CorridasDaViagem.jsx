import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { CampoReais } from '../../componentes/CampoReais'
import { useCorridasDaViagem } from '../../aplicacao/useCorridasDaViagem'
import {
  EMPRESAS_DE_CORRIDA, corpoCorrida, custoDasCorridas, empresaPadraoDaCorrida, erroDoFormularioCorrida,
  formularioDaCorrida, resumoDaCorrida,
} from '../../dominio/entregasApi'
import { centavosDoTexto, lerMoeda, moeda, textoDosCentavos } from '../../dominio/formato'
import css from './entregasApi.module.css'

// Corrida pedida por fora (I9.2, #1691): a atendente chama a 99, a Lalamove ou a Uber pelo app e
// anota aqui a empresa, o custo e o motorista. Três toques: abrir, digitar o custo, salvar. O
// sistema não cota nem chama corrida; só guarda o que foi pago, para o custo por entrega aparecer.
export function CorridasDaViagem({ viagem, entregadores, ocupado }) {
  const { corridas, erro, enviando, registrar, corrigir } = useCorridasDaViagem(viagem.id)
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
          {corridas.map((corrida) => (
            <li key={corrida.id} className={`${css.cartao} ${css.linha}`}>
              <span className={css.cresce}>{resumoDaCorrida(corrida)}</span>
              <Botao variante="texto" disabled={travado} aria-label={`Corrigir corrida ${resumoDaCorrida(corrida)}`} onClick={() => abrirCorrecao(corrida)}>Corrigir</Botao>
            </li>
          ))}
        </ul>
      )}
      {(corridas ?? []).length > 1 && <p className={css.apoio}>Custo das corridas desta viagem: <strong>{moeda(total / 100)}</strong></p>}
      {erro && <p className={css.erroCampo} role="alert">{erro}</p>}

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
          {corridas !== null && corridas.length === 0 && <span className={css.apoio}>Chamou 99, Lalamove ou Uber pelo app? Anote o custo aqui.</span>}
        </div>
      )}
    </section>
  )
}
