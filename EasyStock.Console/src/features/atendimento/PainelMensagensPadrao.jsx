import { useEffect, useId, useRef, useState } from 'react'
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

export function PainelMensagensPadrao({ conversa, aoFechar }) {
  const { listarModelos, enviarModelo } = useAcoes()
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
  const ocupado = useRef(false)

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
    if (ocupado.current || falta) return
    ocupado.current = true; setEnviando(true); setErro(null)
    try {
      const m = await enviarModelo(conversa.id, { nome: escolhido.nome, idioma: escolhido.idioma, parametros: parametros.map((p) => p.trim()) })
      if (['Enviada', 'Entregue', 'Lida'].includes(m.status)) aoFechar()
      else setErro(m.erro || 'Envio sem confirmação. Confira a conversa no WhatsApp antes de enviar de novo.')
    } catch (e) { setErro(erroDoModelo(e, { envio: true, podeIntegrar }).texto) }
    finally { ocupado.current = false; setEnviando(false) }
  }

  return (
    <Popover rotulo="Mensagens padrão do WhatsApp" papel="group" aoFechar={aoFechar} portal folha>
      <div className={css.seletor}>
        {lista === null && <output className={css.vazio}>Carregando as mensagens padrão…</output>}
        {erroLista && <p className={css.erroPainel} role="alert">{erroLista}</p>}
        {lista?.length === 0 && !erroLista && (
          <div className={css.vazio} data-vazio>
            <p>Nenhuma mensagem padrão cadastrada</p>
            {podeGerenciar
              ? <a className={css.gerenciar} href={ROTA_GESTAO_RESPOSTAS} onClick={aoFechar}>Cadastrar em Gestão &gt; Respostas</a>
              : <p>Peça a quem gerencia a loja para cadastrar.</p>}
          </div>
        )}
        {!escolhido && Boolean(lista?.length) && (
          <ul id={idLista} aria-label="Mensagens padrão" className={css.lista}>
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
          <fieldset className={`${css.modeloCampos} ${erro ? css.modeloCamposComErro : ''}`} disabled={enviando}>
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
        <div className={css.rodape}>
          {escolhido
            ? <Botao disabled={enviando} onClick={() => { setEscolhido(null); setErro(null) }}>Voltar à lista</Botao>
            : <span className={css.dica}>Fora da janela só sai mensagem padrão</span>}
          {escolhido && <Botao variante="primario" disabled={enviando || falta} onClick={enviar}>{enviando ? 'Enviando…' : 'Enviar'}</Botao>}
        </div>
      </div>
    </Popover>
  )
}
