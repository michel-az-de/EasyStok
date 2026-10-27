// Aba Avisos da Gestão (#2096, só no modo API): quais situações da operação chamam a dona e o
// gerente por aviso no aparelho, e-mail e sino. Lê e grava `api/atendimento/avisos`; faixas,
// padrões e o corpo do PUT moram em `dominio/avisosDaOperacao.js`.
import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { carregarAvisos, gravarAvisos } from '../../../aplicacao/avisosDaOperacao'
import { Botao } from '../../../componentes/Botao'
import {
  GRUPOS_DE_AVISOS, avisoLigado, avisosMudaram, ligarAviso, mudarPrazo, textoDaFaixa, textoDoSla, validarAvisos,
} from '../../../dominio/avisosDaOperacao'
import css from './abaAvisos.module.css'

function LinhaDoAviso({ aviso, avisos, erro, desabilitado, aoMudar }) {
  const id = useId()
  const ligado = avisoLigado(avisos, aviso.campo)
  const sla = aviso.campo === 'clienteSemResposta' ? textoDoSla(avisos.slaRespostaMinutos) : null
  const dica = sla ?? aviso.dica
  return (
    <li className={css.linha}>
      <label className={css.interruptor} htmlFor={`${id}-ligado`}>
        <input
          id={`${id}-ligado`}
          type="checkbox"
          role="switch"
          checked={ligado}
          aria-checked={ligado}
          disabled={desabilitado}
          aria-describedby={dica ? `${id}-dica` : undefined}
          onChange={(evento) => aoMudar(ligarAviso(avisos, aviso.campo, evento.target.checked))}
        />
        <span className={css.rotulo}>{aviso.rotulo}</span>
      </label>
      {dica && <p id={`${id}-dica`} className={css.dica}>{dica}</p>}
      {aviso.tipo === 'prazo' && ligado && (
        <div className={css.prazo}>
          <label className={css.rotuloPrazo} htmlFor={`${id}-prazo`}>{aviso.antes ? 'Quanto antes' : 'Depois de'}</label>
          <div className={css.numeroComUnidade}>
            <input
              id={`${id}-prazo`}
              className={css.numero}
              type="number"
              inputMode="numeric"
              min={aviso.min}
              max={aviso.max}
              step={1}
              value={avisos[aviso.campo]}
              disabled={desabilitado}
              aria-invalid={erro ? true : undefined}
              aria-describedby={`${id}-faixa`}
              onChange={(evento) => aoMudar(mudarPrazo(avisos, aviso.campo, evento.target.value))}
            />
            <span className={css.unidade}>{aviso.unidade.curta}</span>
          </div>
          <p id={`${id}-faixa`} className={erro ? css.erro : css.dica}>{erro ?? textoDaFaixa(aviso)}</p>
        </div>
      )}
    </li>
  )
}

function Retorno({ situacao }) {
  if (situacao.estado === 'salvando') return <output className={css.retorno}>Salvando…</output>
  if (situacao.estado === 'salvo') return <output className={css.retorno}>Salvo no EasyStok.</output>
  if (situacao.estado === 'erro') return <output className={`${css.retorno} ${css.erro}`} role="alert">Não foi possível salvar. {situacao.mensagem}</output>
  return null
}

function FormularioDeAvisos() {
  const [carga, setCarga] = useState({ estado: 'carregando' })
  const [salvo, setSalvo] = useState(null)
  const [rascunho, setRascunho] = useState(null)
  const [situacao, setSituacao] = useState({ estado: 'ocioso' })
  const [conferir, setConferir] = useState(false)
  const consulta = useRef(0)
  const avisos = rascunho ?? salvo

  const buscar = useCallback(() => {
    const atual = ++consulta.current
    return carregarAvisos()
      .then((dados) => {
        if (atual !== consulta.current) return
        setSalvo(dados)
        setCarga({ estado: 'ok' })
      })
      .catch((erro) => { if (atual === consulta.current) setCarga({ estado: 'erro', mensagem: erro.message }) })
  }, [])

  useEffect(() => {
    buscar()
    // Resposta que chega depois de sair da aba não mexe no estado.
    const leituras = consulta
    return () => { leituras.current++ }
  }, [buscar])

  if (carga.estado === 'carregando') return <p className={css.descricao}>Carregando os avisos…</p>
  if (carga.estado === 'erro') {
    return (
      <div className={css.secao} role="alert">
        <p className={css.erro}>Os avisos não carregaram: {carga.mensagem}</p>
        <div className={css.acoes}>
          <Botao className={css.toque} onClick={() => { setCarga({ estado: 'carregando' }); buscar() }}>Tentar novamente</Botao>
        </div>
      </div>
    )
  }

  const salvando = situacao.estado === 'salvando'
  const pendente = avisosMudaram(avisos, salvo)
  const erros = validarAvisos(avisos)
  const comErro = Object.keys(erros).length > 0
  const mudar = (proximos) => {
    setSituacao({ estado: 'ocioso' })
    setRascunho(avisosMudaram(proximos, salvo) ? proximos : null)
  }

  async function aoSalvar(evento) {
    evento.preventDefault()
    if (!pendente || salvando) return
    setConferir(true)
    if (comErro) return
    const atual = ++consulta.current
    setSituacao({ estado: 'salvando' })
    try {
      const confirmado = await gravarAvisos(avisos)
      if (atual === consulta.current) setSalvo(confirmado)
      setRascunho(null)
      setConferir(false)
      setSituacao({ estado: 'salvo' })
    } catch (erro) {
      setSituacao({ estado: 'erro', mensagem: erro.message })
    }
  }

  return (
    <form className={css.formulario} onSubmit={aoSalvar} noValidate>
      {GRUPOS_DE_AVISOS.map((grupo) => (
        <fieldset key={grupo.id} className={css.grupo}>
          <legend className={css.tituloGrupo}>{grupo.titulo}</legend>
          <ul className={css.lista}>
            {grupo.avisos.map((aviso) => (
              <LinhaDoAviso
                key={aviso.campo}
                aviso={aviso}
                avisos={avisos}
                erro={conferir || avisos[aviso.campo] !== '' ? erros[aviso.campo] : null}
                desabilitado={salvando}
                aoMudar={mudar}
              />
            ))}
          </ul>
        </fieldset>
      ))}
      {pendente && !salvando && <output className={css.pendencia}>Alterações não salvas.</output>}
      {conferir && comErro && <p className={css.erro} role="alert">Confira os prazos marcados antes de salvar.</p>}
      <div className={css.acoes}>
        <Botao variante="primario" tipo="submit" className={css.toque} disabled={!pendente || salvando}>Salvar avisos</Botao>
        <Botao variante="texto" disabled={!pendente || salvando} onClick={() => { setRascunho(null); setConferir(false); setSituacao({ estado: 'ocioso' }) }}>
          Desfazer edição
        </Botao>
        <Retorno situacao={situacao} />
      </div>
    </form>
  )
}

export function AbaAvisos() {
  const { acoes } = useAcessoModulos()
  return (
    <div className={css.aba}>
      <p className={css.introducao}>
        Vão para a dona e o gerente por aviso no aparelho, e-mail e sino. Para receber no aparelho, ative em
        {' '}&lsquo;Ativar avisos neste aparelho&rsquo; no sino.
      </p>
      {acoes.editarAtendimento === true
        ? <FormularioDeAvisos />
        : <p className={css.descricao}>Os avisos são configurados pela dona.</p>}
    </div>
  )
}
