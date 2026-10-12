import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { Modal } from '../../componentes/Modal'
import { useAcoes } from '../../aplicacao/contextos'
import css from './atendimento.module.css'

export function ModalEnviarModelo({ conversa, aoFechar }) {
  const { listarModelos, enviarModelo, prepararOutroModelo } = useAcoes()
  const [lista, setLista] = useState(null)
  const [escolha, setEscolha] = useState('')
  const [parametros, setParametros] = useState([])
  const [erro, setErro] = useState(null)
  const [enviando, setEnviando] = useState(false)
  const [resultado, setResultado] = useState(null)
  const [conferido, setConferido] = useState(false)
  const ocupado = useRef(false)
  const consulta = useRef(0)
  async function carregar(atualizar = false) {
    const versao = ++consulta.current
    setLista(null); setErro(null); setEscolha(''); setParametros([])
    try {
      const modelos = await listarModelos(atualizar)
      if (versao === consulta.current) setLista(modelos)
    } catch (e) { if (versao === consulta.current) { setLista([]); setErro(e.message) } }
  }
  useEffect(() => {
    carregar()
    return () => { consulta.current++ }
    // A modal é remontada ao trocar a conversa; atualizar a lista é uma ação explícita.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])
  const modelo = lista?.[Number(escolha)]
  const bloqueado = Boolean(conversa.bloqueio) || conversa.estado === 'Encerrado' || !conversa.clienteId
  const falta = escolha === '' || !modelo || modelo.indisponivel || parametros.some(p => !p.trim() || p.includes('{{'))
  const previa = modelo ? [modelo.cabecalho, modelo.corpo.replace(/\{\{([1-9][0-9]?)\}\}/g,
    (original, n) => parametros[Number(n) - 1]?.trim() || original), modelo.rodape].filter(Boolean).join('\n') : ''
  const fechar = () => { if (!ocupado.current) aoFechar() }
  async function enviar() {
    if (ocupado.current || falta || bloqueado || resultado) return
    ocupado.current = true; setEnviando(true); setErro(null)
    try {
      const m = await enviarModelo(conversa.id, { nome: modelo.nome, idioma: modelo.idioma, parametros })
      if (['Enviada', 'Entregue', 'Lida'].includes(m.status)) aoFechar()
      else {
        setResultado(m)
        setErro(m.erro || 'Envio sem confirmação. Confira a conversa no WhatsApp antes de iniciar outro envio.')
      }
    } catch (e) { setErro(`${e.message} Ao tentar novamente, esta mesma tentativa será consultada sem duplicar o envio.`) }
    finally { ocupado.current = false; setEnviando(false) }
  }
  return (
    <Modal titulo="Enviar modelo aprovado" descricao={`Envie para ${conversa.nome}. O texto livre continua bloqueado fora das 24 horas até o cliente responder.`}
      aoFechar={fechar} rodape={<>
        <Botao disabled={enviando} onClick={fechar}>Fechar</Botao>
        <Botao variante="primario" disabled={enviando || Boolean(falta) || bloqueado || Boolean(resultado)} onClick={enviar}>
          {enviando ? 'Enviando…' : 'Enviar modelo'}
        </Botao>
      </>}>
      <div className={css.programar}>
        {bloqueado && <p role="alert">Cadastre o cliente e confira se a conversa está aberta e desbloqueada na Ficha.</p>}
        <Botao onClick={() => carregar(true)} disabled={enviando || Boolean(resultado)}>Atualizar modelos</Botao>
        {lista === null && <output>Carregando modelos aprovados…</output>}
        {lista?.length === 0 && !erro && <p>Nenhum modelo aprovado nesta conta. Crie e aprove o modelo no Gerenciador da Meta.</p>}
        {Boolean(lista?.length) && <fieldset className={css.programarCampos} disabled={enviando || bloqueado || Boolean(resultado)}>
          <CampoSelecao rotulo="Modelo aprovado" value={escolha} opcoes={[
            { valor: '', rotulo: 'Selecione um modelo' },
            ...lista.map((m, i) => ({ valor: String(i), rotulo: `${m.nome} · ${m.idioma}${m.indisponivel ? ' · indisponível aqui' : ''}` })),
          ]} onChange={e => {
            setEscolha(e.target.value)
            setParametros(Array(lista[Number(e.target.value)]?.quantidadeParametros ?? 0).fill(''))
          }} />
          {escolha !== '' && modelo && <>
            <p>{modelo.categoria === 'MARKETING' ? 'Novidades e promoções: exige autorização de marketing na Ficha.' : 'Aviso transacional: respeita os consentimentos da Ficha.'}</p>
            {modelo.indisponivel && <p role="alert" className={css.programarErro}>{modelo.indisponivel}</p>}
            {!modelo.indisponivel && parametros.map((p, i) => <CampoTexto key={i} rotulo={`Parâmetro ${i + 1}`} value={p} maxLength={1024}
              onChange={e => setParametros(atuais => atuais.map((v, n) => n === i ? e.target.value : v))} />)}
            <p className={css.cardapioRotulo}>Prévia da mensagem</p>
            <p className={css.programadaTexto}>{previa}</p>
          </>}
        </fieldset>}
        {erro && <p className={css.programarErro} role="alert">{erro}</p>}
        {resultado && <>
          <label className={css.programarOpcao}><input type="checkbox" checked={conferido} onChange={e => setConferido(e.target.checked)} />
            Conferi no WhatsApp e preciso iniciar outro envio.
          </label>
          <Botao disabled={!conferido} onClick={() => {
            prepararOutroModelo(conversa.id); setResultado(null); setErro(null); setConferido(false)
          }}>Preparar outro envio</Botao>
        </>}
      </div>
    </Modal>
  )
}
