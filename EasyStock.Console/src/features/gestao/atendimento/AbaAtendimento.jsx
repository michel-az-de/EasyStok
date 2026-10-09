// Aba Atendimento da Gestão (F02, só no modo API): o expediente da loja (S40) e a
// configuração do atendimento (S08) gravados no EasyStok. O horário por dia continua no
// modal Automáticas da barra lateral, onde a dona já o achava; aqui ficam o controle manual, as
// mensagens de loja fechada e o jeito do automático falar.
import { useCallback, useEffect, useRef, useState } from 'react'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { Botao } from '../../../componentes/Botao'
import { CampoArea, CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { useAcoes, useAtendimento } from '../../../aplicacao/contextos'
import css from './abaAtendimento.module.css'

const TAMANHO_MENSAGEM = 500

const NIVEIS_DE_SUGESTAO = [
  { valor: 'Discreto', rotulo: 'Discreto: só responde o que perguntam' },
  { valor: 'Ativo', rotulo: 'Ativo: oferece cardápio, adicionais e lembretes' },
]

const OCIOSO = { estado: 'ocioso' }

// Situação de um "Salvar", mostrada ao lado do botão: o erro da API nunca some calado.
function Retorno({ situacao }) {
  if (situacao.estado === 'salvando') return <output className={css.retorno}>Salvando…</output>
  if (situacao.estado === 'salvo') return <output className={css.retorno}>Salvo no EasyStok.</output>
  if (situacao.estado === 'erro') {
    return <output className={`${css.retorno} ${css.erro}`} role="alert">Falha ao salvar: {situacao.mensagem}</output>
  }
  return null
}

function useSalvar() {
  const [situacao, setSituacao] = useState(OCIOSO)
  const ocupado = useRef(false)
  const salvar = async (chamada) => {
    if (ocupado.current) return { ok: false }
    ocupado.current = true
    setSituacao({ estado: 'salvando' })
    try {
      const resultado = await chamada()
      setSituacao({ estado: 'salvo' })
      return { ok: true, resultado }
    } catch (erro) {
      setSituacao({ estado: 'erro', mensagem: erro.message })
      return { ok: false }
    } finally {
      ocupado.current = false
    }
  }
  return [situacao, salvar, () => setSituacao(OCIOSO)]
}

function LojaAgora() {
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
          ? 'Segue o horário configurado. O horário de cada dia se ajusta no Balcão, botão Automáticas (raio).'
          : `Forçada ${lojaAberta ? 'aberta' : 'fechada'} na mão. Só volta ao horário quando você mandar.`}
      </p>
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
  const { salvarMensagensExpediente } = useAcoes()
  const [rascunho, setRascunho] = useState(null)
  const [situacao, salvar, limpar] = useSalvar()

  const valor = rascunho ?? {
    mensagemForaDoHorario: expediente.mensagemForaDoHorario,
    mensagemLojaFechada: expediente.mensagemLojaFechada,
  }
  const mudar = (campo) => (e) => {
    limpar()
    setRascunho({ ...valor, [campo]: e.target.value })
  }

  async function aoSalvar(evento) {
    evento.preventDefault()
    const { ok } = await salvar(() => salvarMensagensExpediente(valor))
    if (ok) setRascunho(null)
  }

  return (
    <form className={css.secao} aria-labelledby="gestao-atend-expediente" onSubmit={aoSalvar}>
      <h3 id="gestao-atend-expediente">Quando a loja não atende</h3>
      {!expediente.carregado && <p className={css.descricao}>Carregando o expediente…</p>}
      <CampoArea
        rotulo="Fora do horário"
        dica="O cliente recebe isto na primeira mensagem fora do horário, no lugar da saudação."
        maxLength={TAMANHO_MENSAGEM}
        value={valor.mensagemForaDoHorario}
        onChange={mudar('mensagemForaDoHorario')}
      />
      <CampoArea
        rotulo="Loja fechada"
        dica="O cliente recebe isto na primeira mensagem com a loja fechada na mão, no lugar da saudação."
        maxLength={TAMANHO_MENSAGEM}
        value={valor.mensagemLojaFechada}
        onChange={mudar('mensagemLojaFechada')}
      />
      <div className={css.acoes}>
        <Botao
          variante="primario"
          tipo="submit"
          className={css.toque}
          disabled={!rascunho || situacao.estado === 'salvando'}
        >
          Salvar mensagens
        </Botao>
        <Retorno situacao={situacao} />
      </div>
    </form>
  )
}

function ConfiguracaoDoAtendimento() {
  const { carregarConfiguracao, salvarConfiguracao } = useAcoes()
  const [carga, setCarga] = useState({ estado: 'carregando' })
  const [form, setForm] = useState(null)
  const [situacao, salvar, limpar] = useSalvar()
  const consulta = useRef(0)

  const buscar = useCallback(() => {
    const atual = ++consulta.current
    return carregarConfiguracao()
      .then((c) => {
        if (atual !== consulta.current) return
        // Consulta repetida ou atrasada não substitui um rascunho já aberto.
        setForm((anterior) => anterior ?? c)
        setCarga({ estado: 'ok' })
      })
      .catch((erro) => {
        if (atual === consulta.current) setCarga({ estado: 'erro', mensagem: erro.message })
      })
  }, [carregarConfiguracao])

  useEffect(() => {
    buscar()
    return () => { consulta.current++ }
  }, [buscar])

  const tentarDeNovo = () => {
    setCarga({ estado: 'carregando' })
    buscar()
  }

  if (carga.estado === 'carregando') return <p className={css.descricao}>Carregando a configuração…</p>
  if (carga.estado === 'erro') {
    return (
      <div className={css.secao} role="alert">
        <p className={css.erro}>A configuração não carregou: {carga.mensagem}</p>
        <div className={css.acoes}>
          <Botao className={css.toque} onClick={tentarDeNovo}>Tentar de novo</Botao>
        </div>
      </div>
    )
  }

  const mudar = (campo, lerValor = (e) => e.target.value) => (e) => {
    limpar()
    setForm({ ...form, [campo]: lerValor(e) })
  }

  async function aoSalvar(evento) {
    evento.preventDefault()
    // A resposta é o que ficou gravado: o formulário passa a mostrar isso.
    const { ok, resultado } = await salvar(() => salvarConfiguracao(form))
    if (ok && resultado) setForm(resultado)
  }

  return (
    <form className={css.secao} aria-labelledby="gestao-atend-config" onSubmit={aoSalvar}>
      <fieldset disabled={situacao.estado === 'salvando'} className={css.campos}>
        <div className={css.topo}>
          <h3 id="gestao-atend-config">Como o automático atende</h3>
          <label className={css.chave}>
            <input
              type="checkbox"
              checked={form.ativo}
              aria-describedby="gestao-atend-config-efeito"
              onChange={mudar('ativo', (e) => e.target.checked)}
            />
            Atendimento automático {form.ativo ? 'ligado' : 'desligado'}
          </label>
        </div>
        {/* #1475: o backend lê este campo; desligado, o agente não responde e a conversa vai para a dona. */}
        <p id="gestao-atend-config-efeito" className={css.descricao}>
          {form.ativo
            ? 'O agente responde o cliente no WhatsApp e passa a conversa para você quando precisa.'
            : 'O agente não responde: toda conversa que chegar vai direto para você. A saudação continua saindo.'}
        </p>
        <CampoTexto
          rotulo="Tom"
          dica="Curto, por exemplo: acolhedor, direto, sem gíria."
          value={form.tom}
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
          onChange={mudar('saudacaoPrimeiroContato')}
        />
        <CampoArea rotulo="Saudação para quem volta" value={form.saudacaoRetorno} onChange={mudar('saudacaoRetorno')} />
        <CampoArea
          rotulo="Frase de espera"
          dica="Quando o automático passa a conversa para você."
          value={form.fraseEspera}
          onChange={mudar('fraseEspera')}
        />
        <CampoArea rotulo="Endereço fora da área" value={form.mensagemForaArea} onChange={mudar('mensagemForaArea')} />
        <CampoTexto
          rotulo="Modelo de retomada (WhatsApp)"
          dica="Nome do modelo aprovado na Meta, com uma variável: o primeiro nome do cliente. Sai quando a janela de 24 h venceu; a mensagem que falhou vai quando o cliente responder. Vazio: sem retomada."
          value={form.modeloRetomadaNome ?? ''}
          onChange={mudar('modeloRetomadaNome')}
        />
        <div className={css.numeros}>
          <CampoTexto
            rotulo="Respiro entre janelas (min)"
            tipo="number"
            min={0}
            inputMode="numeric"
            value={form.respiroMinutos}
            onChange={mudar('respiroMinutos')}
          />
          <CampoTexto
            rotulo="Preparo padrão (min)"
            tipo="number"
            min={1}
            inputMode="numeric"
            value={form.tempoPreparoPadraoMinutos}
            onChange={mudar('tempoPreparoPadraoMinutos')}
          />
          {/* #1427: um prazo só para a loja, todos os canais. Estourado, o
              cartão pisca no Balcão; fora do expediente a contagem para. */}
          <CampoTexto
            rotulo="Prazo de primeira resposta (min)"
            dica="De 1 a 240. Passou disso, a conversa pisca. Fora do horário da loja o prazo para de contar."
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
          <Botao variante="primario" tipo="submit" className={css.toque} disabled={situacao.estado === 'salvando'}>
            Salvar configuração
          </Botao>
          <Retorno situacao={situacao} />
        </div>
      </fieldset>
    </form>
  )
}

export function AbaAtendimento() {
  const { recarregarExpediente } = useAcoes()
  const { acoes } = useAcessoModulos()
  // O expediente pode ter mudado em outro aparelho: abrir a aba traz o de agora.
  useEffect(() => { recarregarExpediente() }, [recarregarExpediente])
  return (
    <div className={css.aba}>
      <LojaAgora />
      {acoes.editarAtendimento === true ? <>
        <MensagensDoExpediente />
        <ConfiguracaoDoAtendimento />
      </> : <p className={css.descricao}>Horários, mensagens e prazo de resposta são configurados pela dona.</p>}
    </div>
  )
}
