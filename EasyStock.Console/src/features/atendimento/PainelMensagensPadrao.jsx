import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoTexto } from '../../componentes/Campo'
import { Popover } from '../../componentes/Popover'
import { algumPassaDoLimite } from '../../componentes/limiteDoCampo'
import { useAcoes } from '../../aplicacao/contextos'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { podeEditarRespostas, podeVerIntegracoes } from '../../dominio/acessoModulos'
import { erroDoModelo } from '../../dominio/falhaDeEnvio'
import { hashDoModulo } from '../../dominio/modulos'
import { nomeLegivel, numerosDosParametros, parametrosCompletos, previaDoModelo } from '../../dominio/mensagensPadrao'
import css from './seletorRespostas.module.css'

// #3206: com a janela fechada, o botão Respostas abre esta lista no próprio compositor (sem modal). Mostra as
// mensagens padrão do catálogo; escolhida uma, pede um campo por {{n}}, mostra a prévia ao vivo e envia pela
// ação de modelo de sempre. O servidor decide o que vale (cliente sem escrita há 7 dias etc.) e a frase dele aparece.
const LIMITE_PARAMETRO = 1024
const ROTA_GESTAO_RESPOSTAS = hashDoModulo('atendimento', 'respostas')

export function PainelMensagensPadrao({ conversa, aoFechar, janelaFechada = false }) {
  const { listarModelos, enviarModelo, prepararOutroModelo } = useAcoes()
  const { acoes } = useAcessoModulos()
  const podeGerenciar = podeEditarRespostas(acoes)
  const podeIntegrar = podeVerIntegracoes(acoes)
  const idLista = useId()
  const [lista, setLista] = useState(null)
  const [erroLista, setErroLista] = useState(null)
  const [escolhido, setEscolhido] = useState(null)
  const [parametros, setParametros] = useState([])
  const [erro, setErro] = useState(null)
  const [enviando, setEnviando] = useState(false)
  // Envio sem confirmação (a modal antiga guardava o `resultado`): a primeira tentativa pode estar pendente, então
  // Enviar e campos ficam travados até a atendente conferir no WhatsApp e pedir "Enviar outro".
  const [semConfirmacao, setSemConfirmacao] = useState(false)
  const [conferido, setConferido] = useState(false)
  const ocupado = useRef(false)
  const listaRef = useRef(null)
  const avisoRef = useRef(null)
  const camposRef = useRef(null)
  const enviarRef = useRef(null)
  const conferidoRef = useRef(null)
  const origem = useRef(globalThis.document?.activeElement ?? null)
  // O Popover fecha por clique fora e por Esc sem dizer qual; o pointerdown em captura marca a origem.
  const viaPonteiro = useRef(false)
  const estado = useRef({})
  estado.current = { enviando, protegido: parametros.some((p) => p.trim()) || Boolean(erro) || semConfirmacao }
  useEffect(() => {
    const marcar = () => { viaPonteiro.current = true; setTimeout(() => { viaPonteiro.current = false }, 0) }
    const teclou = () => { viaPonteiro.current = false }
    document.addEventListener('pointerdown', marcar, true)
    document.addEventListener('keydown', teclou, true)
    return () => { document.removeEventListener('pointerdown', marcar, true); document.removeEventListener('keydown', teclou, true) }
  }, [])
  const fecharDoPopover = useCallback(() => {
    if (estado.current.enviando) return
    if (viaPonteiro.current && estado.current.protegido) return
    aoFechar()
  }, [aoFechar])

  // Foco: lista carregada (ou aviso), depois o primeiro campo ao escolher, e a lista de volta ao voltar.
  useEffect(() => {
    if (escolhido) (camposRef.current?.querySelector('input, textarea') ?? enviarRef.current)?.focus()
    else if (lista !== null) (listaRef.current?.querySelector('button:not(:disabled)') ?? avisoRef.current)?.focus()
  }, [escolhido, lista])
  useEffect(() => { if (semConfirmacao) conferidoRef.current?.focus() }, [semConfirmacao])

  useEffect(() => {
    let vivo = true
    listarModelos(false).then((m) => { if (vivo) setLista(m ?? []) })
      .catch((e) => { if (vivo) { setLista([]); setErroLista(erroDoModelo(e, { podeIntegrar }).texto) } })
    return () => { vivo = false }
    // Carrega uma vez por abertura; a conversa troca com o painel fechado.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  function escolher(modelo) {
    setEscolhido(modelo); setErro(null)
    setParametros(Array(numerosDosParametros(modelo).length).fill(''))
  }
  const numeros = escolhido ? numerosDosParametros(escolhido) : []
  const previa = escolhido ? previaDoModelo(escolhido, parametros) : null
  const falta = !escolhido || Boolean(escolhido.indisponivel) || !parametrosCompletos(escolhido, parametros)
    || algumPassaDoLimite(...parametros.map((p) => [p, LIMITE_PARAMETRO]))

  async function enviar() {
    if (ocupado.current || falta || semConfirmacao) return
    ocupado.current = true; setEnviando(true); setErro(null)
    try {
      const m = await enviarModelo(conversa.id, { nome: escolhido.nome, idioma: escolhido.idioma, parametros: parametros.map((p) => p.trim()) })
      if (['Enviada', 'Entregue', 'Lida'].includes(m.status)) {
        aoFechar()
        if (origem.current?.isConnected) origem.current.focus()
      } else { setSemConfirmacao(true); setErro(m.erro || 'Envio sem confirmação. Confira a conversa no WhatsApp antes de enviar de novo.') }
    } catch (e) { setErro(erroDoModelo(e, { envio: true, podeIntegrar }).texto) }
    finally { ocupado.current = false; setEnviando(false) }
  }

  function prepararOutro() {
    prepararOutroModelo?.(conversa.id)
    setSemConfirmacao(false); setConferido(false); setErro(null)
  }

  return (
    <Popover rotulo="Mensagens padrão do WhatsApp" papel="group" aoFechar={fecharDoPopover} portal folha>
      <div className={css.seletor}>
        {lista === null && <output className={css.vazio}>Carregando as mensagens padrão…</output>}
        {erroLista && <p ref={avisoRef} tabIndex={-1} className={css.erroPainel} role="alert">{erroLista}</p>}
        {lista?.length === 0 && !erroLista && (
          <div ref={avisoRef} tabIndex={-1} className={css.vazio} data-vazio>
            <p>Nenhuma mensagem padrão cadastrada</p>
            {podeGerenciar
              ? <a className={css.gerenciar} href={ROTA_GESTAO_RESPOSTAS} onClick={aoFechar}>Cadastrar em Gestão &gt; Respostas</a>
              : <p>Peça a quem gerencia a loja para cadastrar.</p>}
          </div>
        )}
        {!escolhido && Boolean(lista?.length) && (
          <ul ref={listaRef} id={idLista} aria-label="Mensagens padrão" className={css.lista}>
            {lista.map((m) => (
              <li key={`${m.nome}-${m.idioma}`} className={css.linha}>
                <Botao className={css.itemModelo} disabled={Boolean(m.indisponivel)} onClick={() => escolher(m)}>
                  <span className={css.titulo}>{nomeLegivel(m.nome)}</span>
                  <span className={css.previa}>{m.indisponivel || m.corpo}</span>
                </Botao>
              </li>
            ))}
          </ul>
        )}
        {escolhido && (
          <fieldset ref={camposRef} className={`${css.modeloCampos} ${erro ? css.modeloCamposComErro : ''}`} disabled={enviando || semConfirmacao}>
            <legend className={css.titulo}>{nomeLegivel(escolhido.nome)}</legend>
            {numeros.map((n, i) => <CampoTexto key={n} rotulo={`${n}º campo`} value={parametros[i] ?? ''} limite={LIMITE_PARAMETRO}
              onChange={(e) => setParametros((atuais) => atuais.map((v, k) => k === i ? e.target.value : v))} />)}
            <p className={css.rotuloPrevia}>Prévia da mensagem</p>
            <div className={css.previaCaixa} aria-live="polite">
              {previa.cabecalho && <p className={css.titulo}>{previa.cabecalho}</p>}
              <p>{previa.corpo}</p>
              {previa.rodape && <p className={css.rotuloPrevia}>{previa.rodape}</p>}
            </div>
          </fieldset>
        )}
        {erro && <p className={css.erroPainel} role="alert">{erro}</p>}
        {semConfirmacao && (
          <div className={css.conferir}>
            <label className={css.conferirOpcao}>
              <input ref={conferidoRef} type="checkbox" checked={conferido} onChange={(e) => setConferido(e.target.checked)} />
              Conferi no WhatsApp e preciso enviar outra mensagem.
            </label>
            <Botao disabled={!conferido} onClick={prepararOutro}>Enviar outro</Botao>
          </div>
        )}
        <div className={css.rodape}>
          {escolhido
            ? <Botao disabled={enviando || semConfirmacao} onClick={() => { setEscolhido(null); setErro(null); setParametros([]) }}>Voltar à lista</Botao>
            : <span className={css.dica}>{janelaFechada ? 'Fora da janela só sai mensagem padrão' : ''}</span>}
          {escolhido && <Botao ref={enviarRef} variante="primario" disabled={enviando || semConfirmacao || falta} onClick={enviar}>{enviando ? 'Enviando…' : 'Enviar'}</Botao>}
        </div>
      </div>
    </Popover>
  )
}
