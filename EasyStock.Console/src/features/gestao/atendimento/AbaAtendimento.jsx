// Aba Atendimento da Gestão (F02, só no modo API): o expediente da loja (S40) e a
// configuração do atendimento (S08) gravados no EasyStok. Os horários chegam pela
// composição do App, sem acoplar os editores de áreas diferentes.
import { useCallback, useEffect, useRef, useState } from 'react'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { usePendenciaAvisada } from '../../../aplicacao/formularioDeAjuste'
import { useGravacaoDoAjuste, useRascunhoDoAjuste } from '../../../aplicacao/rascunhosDosAjustes'
import { Botao } from '../../../componentes/Botao'
import { CampoArea, CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { useAcoes, useAtendimento } from '../../../aplicacao/contextos'
import { hashDoModulo } from '../../../dominio/modulos'
import css from './abaAtendimento.module.css'

const TAMANHO_MENSAGEM = 500

const NIVEIS_DE_SUGESTAO = [
  { valor: 'Discreto', rotulo: 'Discreto: só responde o que perguntam' },
  { valor: 'Ativo', rotulo: 'Ativo: oferece cardápio, adicionais e lembretes' },
]

const OCIOSO = { estado: 'ocioso' }
const CAMPOS_CONFIGURACAO = [
  'ativo', 'tom', 'nivelSugestao', 'saudacaoPrimeiroContato', 'saudacaoRetorno',
  'fraseEspera', 'mensagemForaArea', 'modeloRetomadaNome', 'respiroMinutos',
  'tempoPreparoPadraoMinutos', 'slaRespostaMinutos',
]
const CAMPOS_SEM_O_AGENTE = CAMPOS_CONFIGURACAO.filter((campo) => campo !== 'ativo')
const valorComparavel = (valor) => String(valor ?? '')
const camposMudaram = (atual, salvo, campos) => campos.some((campo) => valorComparavel(atual?.[campo]) !== valorComparavel(salvo?.[campo]))

function Pendencia({ visivel }) {
  return <output className={visivel ? css.pendencia : 'sr'}>{visivel ? 'Alterações não salvas. Rascunho só nesta sessão; salve ou desfaça antes de sair.' : ''}</output>
}

function useAtualizarAposLimparRascunho(rascunho, situacao, recarregar, gravando) {
  const anterior = useRef({ rascunho, gravando })
  useEffect(() => {
    const limpou = anterior.current.rascunho !== null && rascunho === null
    const terminou = anterior.current.gravando && !gravando
    anterior.current = { rascunho, gravando }
    // Uma gravação iniciada antes de sair desta tela pode terminar depois de voltar.
    if ((limpou || terminou) && situacao.estado === 'ocioso') recarregar()
  }, [rascunho, situacao.estado, recarregar, gravando])
}

// Situação de um "Salvar", mostrada ao lado do botão: o erro da API nunca some calado.
function Retorno({ situacao, gravando }) {
  if (gravando && situacao.estado !== 'salvando') return <output className={css.retorno}>Outro ajuste está sendo salvo. Você pode editar; aguarde para salvar.</output>
  if (situacao.estado === 'salvando') return <output className={css.retorno}>Salvando…</output>
  if (situacao.estado === 'salvo') return <output className={css.retorno}>Salvo no EasyStok.</output>
  if (situacao.estado === 'erro') {
    return <output className={`${css.retorno} ${css.erro}`} role="alert">Não foi possível confirmar o salvamento. {situacao.mensagem}</output>
  }
  return null
}

function useSalvar(chave) {
  const [situacao, setSituacao] = useState(OCIOSO)
  const [gravando, iniciar, estaGravando] = useGravacaoDoAjuste(chave)
  const salvar = async (chamada) => {
    const liberar = iniciar()
    if (!liberar) return { ok: false }
    setSituacao({ estado: 'salvando' })
    try {
      const resultado = await chamada()
      setSituacao({ estado: 'salvo' })
      return { ok: true, resultado }
    } catch (erro) {
      setSituacao({ estado: 'erro', mensagem: erro.message })
      return { ok: false }
    } finally {
      liberar()
    }
  }
  return [situacao, salvar, () => setSituacao(OCIOSO), gravando, estaGravando]
}

function LojaAgora({ horariosAtendimento }) {
  const { agora, aberta, lojaAberta } = useAtendimento()
  const { alternarLoja, voltarAoHorario } = useAcoes()
  const { acoes } = useAcessoModulos()
  return (
    <section className={css.secao} aria-labelledby="gestao-atend-loja">
      <div className={css.topo}>
        <h3 id="gestao-atend-loja">Loja agora</h3>
        <Pilula tom={aberta ? 'ok' : 'perigo'}>{aberta ? 'Aberta' : 'Fechada'}</Pilula>
      </div>
      <p className={css.descricao}>
        {lojaAberta == null
          ? 'Segue os horários da semana.'
          : `${lojaAberta ? 'Aberta' : 'Fechada'} manualmente, sem seguir os horários da semana.`}
      </p>
      {horariosAtendimento && <div className={css.acoes}>{horariosAtendimento}</div>}
      <div className={css.acoes}>
        {acoes.controlarLoja === true && <Botao className={css.toque} onClick={() => alternarLoja(agora)}>
          {aberta ? 'Fechar loja agora' : 'Abrir loja agora'}
        </Botao>}
        {acoes.editarAtendimento === true && <Botao className={css.toque} disabled={lojaAberta == null} onClick={voltarAoHorario}>
          Voltar a seguir o horário
        </Botao>}
      </div>
    </section>
  )
}

function MensagensDoExpediente() {
  const { expediente } = useAtendimento()
  const { salvarMensagensExpediente, recarregarExpediente } = useAcoes()
  const [rascunho, setRascunho] = useRascunhoDoAjuste('mensagens-expediente')
  const [situacao, salvar, limpar, gravando] = useSalvar('expediente-atendimento')
  const expedienteDuranteGravacao = useRef(null)

  const valor = rascunho ?? {
    mensagemForaDoHorario: expediente.mensagemForaDoHorario,
    mensagemLojaFechada: expediente.mensagemLojaFechada,
  }
  const pendente = camposMudaram(valor, expediente, ['mensagemForaDoHorario', 'mensagemLojaFechada'])
  useAtualizarAposLimparRascunho(rascunho, situacao, recarregarExpediente, gravando)
  useEffect(() => {
    if (gravando) { expedienteDuranteGravacao.current = expediente; return }
    if (expedienteDuranteGravacao.current === expediente) return
    expedienteDuranteGravacao.current = null
    if (expediente.carregado && rascunho && !pendente) setRascunho((atual) => atual === rascunho ? null : atual)
  }, [expediente, gravando, pendente, rascunho, setRascunho])
  const mudar = (campo) => (e) => {
    limpar()
    const proximo = { ...valor, [campo]: e.target.value }
    setRascunho(gravando || camposMudaram(proximo, expediente, ['mensagemForaDoHorario', 'mensagemLojaFechada']) ? proximo : null)
  }

  async function aoSalvar(evento) {
    evento.preventDefault()
    if (!expediente.carregado || !pendente || gravando || situacao.estado === 'salvando') return
    const { ok } = await salvar(() => salvarMensagensExpediente(valor))
    if (ok) setRascunho((atual) => atual === rascunho ? null : atual)
  }

  return (
    <form className={css.secao} aria-labelledby="gestao-atend-expediente" onSubmit={aoSalvar}>
      <h3 id="gestao-atend-expediente">Quando a loja não atende</h3>
      <Pendencia visivel={pendente && situacao.estado !== 'salvando'} />
      {!expediente.carregado && <output className={css.descricao}>Carregando o expediente…</output>}
      <CampoArea
        rotulo="Fora do horário"
        dica="Substitui a saudação na primeira mensagem fora do horário."
        maxLength={TAMANHO_MENSAGEM}
        value={valor.mensagemForaDoHorario}
        disabled={!expediente.carregado || situacao.estado === 'salvando'}
        onChange={mudar('mensagemForaDoHorario')}
      />
      <CampoArea
        rotulo="Loja fechada"
        dica="Substitui a saudação na primeira mensagem com a loja fechada manualmente."
        maxLength={TAMANHO_MENSAGEM}
        value={valor.mensagemLojaFechada}
        disabled={!expediente.carregado || situacao.estado === 'salvando'}
        onChange={mudar('mensagemLojaFechada')}
      />
      <div className={css.acoes}>
        <Botao
          variante="primario"
          tipo="submit"
          className={css.toque}
          disabled={!expediente.carregado || !pendente || gravando || situacao.estado === 'salvando'}
        >
          Salvar mensagens
        </Botao>
        <Botao variante="texto" disabled={!pendente || situacao.estado === 'salvando'} onClick={() => { setRascunho(null); limpar() }}>
          Desfazer edição das mensagens
        </Botao>
        <Retorno situacao={situacao} gravando={gravando} />
      </div>
    </form>
  )
}

// `comLigaDesliga` falso (#2070): os Primeiros passos ajustam o atendimento sem ligar o agente. O rascunho da visita
// pode trazer o liga e desliga marcado em outra tela: aqui ele não conta como pendência, a gravação reenvia o `ativo`
// gravado e salvar ou desfazer deixa esse liga e desliga no rascunho, para a aba de Atendimento.
function ConfiguracaoDoAtendimento({ comLigaDesliga = true, aoMudarPendencia }) {
  const { carregarConfiguracao, salvarConfiguracao } = useAcoes()
  const [carga, setCarga] = useState({ estado: 'carregando' })
  const [rascunho, setRascunho] = useRascunhoDoAjuste('configuracao-atendimento')
  const [gravado, setGravado] = useState(null)
  const form = rascunho ?? gravado
  const [situacao, salvar, limpar, gravando, estaGravando] = useSalvar('configuracao-atendimento')
  const consulta = useRef(0)

  const buscar = useCallback(() => {
    const atual = ++consulta.current
    const semGravacao = !estaGravando()
    return carregarConfiguracao()
      .then((c) => {
        if (atual !== consulta.current) return
        // Consulta repetida ou atrasada não substitui um rascunho já aberto.
        setGravado(c)
        if (semGravacao && !estaGravando()) setRascunho((atual) => atual && camposMudaram(atual, c, CAMPOS_CONFIGURACAO) ? atual : null)
        setCarga({ estado: 'ok' })
      })
      .catch((erro) => {
        if (atual === consulta.current) setCarga({ estado: 'erro', mensagem: erro.message })
      })
  }, [carregarConfiguracao, setRascunho, estaGravando])

  useEffect(() => {
    buscar()
    return () => { consulta.current++ }
  }, [buscar])

  const tentarDeNovo = () => {
    setCarga({ estado: 'carregando' })
    buscar()
  }

  useAtualizarAposLimparRascunho(rascunho, situacao, buscar, gravando)
  const camposAVista = comLigaDesliga ? CAMPOS_CONFIGURACAO : CAMPOS_SEM_O_AGENTE
  const pendente = carga.estado === 'ok' && camposMudaram(form, gravado, camposAVista)
  usePendenciaAvisada(pendente || gravando, aoMudarPendencia)
  // O que fica no rascunho depois de salvar ou desfazer: nada, ou só o liga e desliga que veio de outra tela.
  const soOLigaDesliga = (base) => (!comLigaDesliga && rascunho && rascunho.ativo !== base.ativo ? { ...base, ativo: rascunho.ativo } : null)

  if (carga.estado === 'carregando') return <p className={css.descricao}>Carregando a configuração…</p>
  if (carga.estado === 'erro') {
    return (
      <div className={css.secao} role="alert">
        <p className={css.erro}>A configuração não carregou: {carga.mensagem}</p>
        <div className={css.acoes}>
          <Botao className={css.toque} onClick={tentarDeNovo}>Tentar novamente</Botao>
        </div>
      </div>
    )
  }

  const mudar = (campo, lerValor = (e) => e.target.value) => (e) => {
    e.target.setCustomValidity?.('')
    limpar()
    const proximo = { ...form, [campo]: lerValor(e) }
    setRascunho(gravando || camposMudaram(proximo, gravado, CAMPOS_CONFIGURACAO) ? proximo : null)
  }

  async function aoSalvar(evento) {
    evento.preventDefault()
    if (!pendente || gravando || situacao.estado === 'salvando') return
    // Texto em branco é ignorado pela API. Impedir aqui evita confirmar uma alteração que não aconteceu.
    for (const campo of evento.currentTarget.querySelectorAll('[required]')) {
      campo.setCustomValidity(campo.value.trim() ? '' : 'Preencha este campo antes de salvar.')
    }
    if (!evento.currentTarget.reportValidity()) return
    // Uma leitura iniciada ao desfazer não pode substituir a confirmação desta gravação.
    let atual
    // A resposta é o que ficou gravado: o formulário passa a mostrar isso.
    const { ok, resultado } = await salvar(async () => {
      atual = ++consulta.current
      const confirmado = await salvarConfiguracao(comLigaDesliga ? form : { ...form, ativo: gravado.ativo })
      if (!confirmado || typeof confirmado.ativo !== 'boolean') throw new Error('O EasyStok não confirmou a configuração. Tente novamente; sua edição continua nesta tela.')
      return confirmado
    })
    if (ok) {
      if (atual === consulta.current) setGravado(resultado)
      setRascunho((atual) => atual === rascunho ? soOLigaDesliga(resultado) : atual)
    }
  }

  return (
    <form className={css.secao} aria-labelledby="gestao-atend-config" onSubmit={aoSalvar}>
      <fieldset disabled={situacao.estado === 'salvando'} className={css.campos}>
        <div className={css.topo}>
          <h3 id="gestao-atend-config">Como o automático atende</h3>
          {comLigaDesliga && <label className={css.chave}>
            <input
              type="checkbox"
              checked={form.ativo}
              aria-describedby="gestao-atend-config-efeito"
              onChange={mudar('ativo', (e) => e.target.checked)}
            />
            Atendimento automático
          </label>}
        </div>
        <Pendencia visivel={pendente && situacao.estado !== 'salvando'} />
        {/* #1475: o backend lê este campo; desligado, o agente não responde e a conversa vai para a dona. */}
        {comLigaDesliga && <p id="gestao-atend-config-efeito" className={css.descricao}>
          {form.ativo
            ? 'Responde no WhatsApp e chama você quando precisa. A mudança só vale depois de salvar.'
            : 'As conversas vão para você; a saudação continua automática. A mudança só vale depois de salvar.'}
        </p>}
        <CampoTexto
          rotulo="Tom"
          dica="Exemplos: acolhedor, direto, sem gírias."
          value={form.tom}
          required
          onChange={mudar('tom')}
        />
        <CampoSelecao
          rotulo="Sugestões"
          opcoes={NIVEIS_DE_SUGESTAO}
          value={form.nivelSugestao}
          onChange={mudar('nivelSugestao')}
        />
        <CampoArea
          rotulo="Saudação no primeiro contato"
          value={form.saudacaoPrimeiroContato}
          required
          onChange={mudar('saudacaoPrimeiroContato')}
        />
        <CampoArea rotulo="Saudação para quem volta" value={form.saudacaoRetorno} required onChange={mudar('saudacaoRetorno')} />
        <CampoArea
          rotulo="Frase de espera"
          dica="Enviada com a saudação, antes da resposta automática."
          value={form.fraseEspera}
          required
          onChange={mudar('fraseEspera')}
        />
        <CampoArea rotulo="Mensagem para endereço fora da área" value={form.mensagemForaArea} required onChange={mudar('mensagemForaArea')} />
        <CampoTexto
          rotulo="Modelo de retomada (WhatsApp)"
          dica="Nome do modelo aprovado pela Meta, com uma variável: primeiro nome do cliente. Retoma o contato quando um envio é bloqueado pelo limite de 24 h. A mensagem pendente segue quando o cliente responder. Vazio desativa."
          value={form.modeloRetomadaNome ?? ''}
          maxLength={512}
          pattern="[a-z0-9_]+"
          title="Use somente letras minúsculas, números e sublinhado (_). Deixe vazio para desativar a retomada."
          onChange={mudar('modeloRetomadaNome')}
        />
        <div className={css.numeros}>
          <CampoTexto
            rotulo="Tempo extra de preparo (min)"
            tipo="number"
            min={0}
            required
            inputMode="numeric"
            value={form.respiroMinutos}
            onChange={mudar('respiroMinutos')}
          />
          <CampoTexto
            rotulo="Preparo padrão (min)"
            tipo="number"
            min={1}
            required
            inputMode="numeric"
            value={form.tempoPreparoPadraoMinutos}
            onChange={mudar('tempoPreparoPadraoMinutos')}
          />
          {/* #1427: um prazo só para a loja, todos os canais. Estourado, o
              cartão pisca no Balcão; fora do expediente a contagem para. */}
          <CampoTexto
            rotulo="Prazo de primeira resposta (min)"
            dica="De 1 a 240 minutos. Ao vencer, a conversa ganha destaque. A contagem pausa fora do horário da loja."
            tipo="number"
            min={1}
            max={240}
            required
            inputMode="numeric"
            value={form.slaRespostaMinutos ?? 5}
            onChange={mudar('slaRespostaMinutos')}
          />
        </div>
        <div className={css.acoes}>
          <Botao variante="primario" tipo="submit" className={css.toque} disabled={!pendente || gravando || situacao.estado === 'salvando'}>
            Salvar configuração
          </Botao>
          <Botao variante="texto" disabled={!pendente || situacao.estado === 'salvando'} onClick={() => { setRascunho(soOLigaDesliga(gravado)); limpar() }}>
            Desfazer edição da configuração
          </Botao>
          <Retorno situacao={situacao} gravando={gravando} />
        </div>
      </fieldset>
    </form>
  )
}

// #2070: a situação do agente, só leitura. Ligar e desligar fica em Atendimento › Horários e mensagens.
function SituacaoDoAgente() {
  const { carregarConfiguracao } = useAcoes()
  const [carga, setCarga] = useState({ estado: 'carregando' })
  const buscar = useCallback(() => carregarConfiguracao()
    .then((c) => setCarga({ estado: 'ok', ativo: c?.ativo === true }))
    .catch((erro) => setCarga({ estado: 'erro', mensagem: erro.message })), [carregarConfiguracao])
  useEffect(() => { buscar() }, [buscar])
  return (
    <section className={css.secao} aria-labelledby="gestao-atend-agente">
      <h3 id="gestao-atend-agente">Situação do agente</h3>
      {carga.estado === 'carregando' && <output className={css.descricao}>Conferindo o atendimento automático…</output>}
      {carga.estado === 'erro' && <p className={css.erro} role="alert">A situação do agente não carregou: {carga.mensagem}</p>}
      {carga.estado === 'ok' && <p className={css.descricao}>Atendimento automático: {carga.ativo ? 'ligado' : 'desligado'}.</p>}
      <p className={css.descricao}>
        Para ligar ou desligar, abra <a href={hashDoModulo('atendimento', 'horarios')}>Atendimento › Horários e mensagens</a>.
      </p>
    </section>
  )
}

// `secoes` (#2070): os Primeiros passos montam só uma parte. 'horarios': a semana (`horariosDaSemana`, do App) e as
// mensagens de quando a loja não atende; 'configuracao': o atendimento sem o liga e desliga do agente; 'agente': a
// situação do agente. Nenhuma delas mostra "Abrir loja agora". Sem `secoes`, a aba de sempre.
export function AbaAtendimento({ horariosAtendimento = null, secoes = null, horariosDaSemana = null, aoMudarPendencia }) {
  const { recarregarExpediente } = useAcoes()
  const { acoes } = useAcessoModulos()
  // O expediente pode ter mudado em outro aparelho: abrir a aba traz o de agora.
  useEffect(() => { recarregarExpediente() }, [recarregarExpediente])
  if (secoes) {
    const administra = acoes.editarAtendimento === true
    return (
      <div className={css.aba}>
        {secoes.includes('horarios') && <>{horariosDaSemana}{administra && <MensagensDoExpediente />}</>}
        {secoes.includes('configuracao') && administra && <ConfiguracaoDoAtendimento comLigaDesliga={false} aoMudarPendencia={aoMudarPendencia} />}
        {secoes.includes('agente') && <SituacaoDoAgente />}
        {!administra && !secoes.includes('agente') && <p className={css.descricao}>Horários, mensagens e prazo de resposta são configurados pela dona.</p>}
      </div>
    )
  }
  return (
    <div className={css.aba}>
      <LojaAgora horariosAtendimento={horariosAtendimento} />
      {acoes.editarAtendimento === true ? <>
        <MensagensDoExpediente />
        <ConfiguracaoDoAtendimento />
      </> : <p className={css.descricao}>Horários, mensagens e prazo de resposta são configurados pela dona.</p>}
    </div>
  )
}
