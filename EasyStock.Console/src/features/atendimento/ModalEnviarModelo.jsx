import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { Modal } from '../../componentes/Modal'
import { useAcoes } from '../../aplicacao/contextos'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { podeVerIntegracoes } from '../../dominio/acessoModulos'
import { erroDoModelo } from '../../dominio/falhaDeEnvio'
import { hashDoModulo } from '../../dominio/modulos'
import css from './atendimento.module.css'

// #1408: com `cliente` no lugar de `conversa`, é o "Chamar no WhatsApp" de quem nunca escreveu: a API abre (ou
// reaproveita) a conversa e `aoIniciar` recebe o id dela para o Balcão abrir.
export function ModalEnviarModelo({ conversa = null, cliente = null, aoFechar, aoIniciar = null }) {
  const { listarModelos, enviarModelo, prepararOutroModelo, iniciarConversaComModelo, prepararOutroModeloDoCliente } = useAcoes()
  const { acoes } = useAcessoModulos()
  const [lista, setLista] = useState(null)
  const [escolha, setEscolha] = useState('')
  const [parametros, setParametros] = useState([])
  const [erro, setErro] = useState(null)
  // #1966: 409 WHATSAPP_NAO_CONECTADO. A frase vem da API; aqui fica o caminho para Integrações.
  const [semConexao, setSemConexao] = useState(false)
  const falhar = (falha) => { setErro(falha.texto); setSemConexao(falha.integracoes) }
  const [enviando, setEnviando] = useState(false)
  const [resultado, setResultado] = useState(null)
  const [conferido, setConferido] = useState(false)
  const ocupado = useRef(false)
  const consulta = useRef(0)
  async function carregar(atualizar = false) {
    const versao = ++consulta.current
    setLista(null); setErro(null); setSemConexao(false); setEscolha(''); setParametros([])
    try {
      const modelos = await listarModelos(atualizar)
      if (versao === consulta.current) setLista(modelos)
    } catch (e) { if (versao === consulta.current) { setLista([]); falhar(erroDoModelo(e)) } }
  }
  useEffect(() => {
    carregar()
    return () => { consulta.current++ }
    // A modal é remontada ao trocar a conversa; atualizar a lista é uma ação explícita.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])
  const modelo = lista?.[Number(escolha)]
  const bloqueado = cliente
    ? !String(cliente.telefone ?? '').trim()
    : Boolean(conversa.bloqueio) || conversa.estado === 'Encerrado' || !conversa.clienteId
  const falta = escolha === '' || !modelo || modelo.indisponivel || parametros.some(p => !p.trim() || p.includes('{{'))
  const previa = modelo ? [modelo.cabecalho, modelo.corpo.replace(/\{\{([1-9][0-9]?)\}\}/g,
    (original, n) => parametros[Number(n) - 1]?.trim() || original), modelo.rodape].filter(Boolean).join('\n') : ''
  const fechar = () => { if (!ocupado.current) aoFechar() }
  async function enviar() {
    if (ocupado.current || falta || bloqueado || resultado) return
    ocupado.current = true; setEnviando(true); setErro(null); setSemConexao(false)
    try {
      const dados = { nome: modelo.nome, idioma: modelo.idioma, parametros }
      const m = cliente
        ? await iniciarConversaComModelo(cliente, dados).then((r) => { aoIniciar?.(r.conversaId); return r.mensagem })
        : await enviarModelo(conversa.id, dados)
      if (['Enviada', 'Entregue', 'Lida'].includes(m.status)) aoFechar()
      else {
        setResultado(m)
        setErro(m.erro || 'Envio sem confirmação. Confira a conversa no WhatsApp antes de iniciar outro envio.')
      }
    } catch (e) { falhar(erroDoModelo(e, { envio: true })) }
    finally { ocupado.current = false; setEnviando(false) }
  }
  return (
    <Modal titulo={cliente ? 'Chamar no WhatsApp' : 'Enviar modelo aprovado'} descricao={cliente
      ? `Primeira mensagem para ${cliente.nome}: sem conversa recente, só sai modelo aprovado. Enviado, a conversa abre no Balcão.`
      : `Confira a prévia antes de enviar para ${conversa.nome}. Este modelo é enviado diretamente pelo canal; não entra no rascunho de respostas rápidas.`}
      aoFechar={fechar} rodape={<>
        <Botao disabled={enviando} onClick={fechar}>Fechar</Botao>
        <Botao variante="primario" disabled={enviando || Boolean(falta) || bloqueado || Boolean(resultado)} onClick={enviar}>
          {enviando ? 'Enviando…' : 'Enviar modelo'}
        </Botao>
      </>}>
      <div className={css.programar}>
        {bloqueado && <p role="alert">{cliente
          ? 'Cadastre um celular válido na Ficha do cliente para chamar no WhatsApp.'
          : 'Cadastre o cliente e confira se a conversa está aberta e desbloqueada na Ficha.'}</p>}
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
            {!modelo.indisponivel && parametros.length > 0 && <p>Preencha os campos numerados e confira cada substituição na prévia abaixo.</p>}
            {!modelo.indisponivel && parametros.map((p, i) => <CampoTexto key={i} rotulo={`Campo ${i + 1} ({{${i + 1}}})`} value={p} maxLength={1024}
              onChange={e => setParametros(atuais => atuais.map((v, n) => n === i ? e.target.value : v))} />)}
            <p className={css.cardapioRotulo}>Prévia da mensagem</p>
            <p className={css.programadaTexto}>{previa}</p>
          </>}
        </fieldset>}
        {erro && <p className={css.programarErro} role="alert">{erro}</p>}
        {erro && semConexao && (podeVerIntegracoes(acoes)
          ? <a href={hashDoModulo('configuracoes', 'integracoes')}>Abrir Integrações</a>
          : <p>Quem administra a loja conecta o WhatsApp em Configurações, Integrações.</p>)}
        {resultado && <>
          <label className={css.programarOpcao}><input type="checkbox" checked={conferido} onChange={e => setConferido(e.target.checked)} />
            Conferi no WhatsApp e preciso iniciar outro envio.
          </label>
          <Botao disabled={!conferido} onClick={() => {
            if (cliente) prepararOutroModeloDoCliente(cliente.id)
            else prepararOutroModelo(conversa.id)
            setResultado(null); setErro(null); setConferido(false)
          }}>Preparar outro envio</Botao>
        </>}
      </div>
    </Modal>
  )
}
