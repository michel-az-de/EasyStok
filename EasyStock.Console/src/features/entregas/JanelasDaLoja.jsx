import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Chip } from '../../componentes/Chip'
import { CampoTexto } from '../../componentes/Campo'
import { Pilula } from '../../componentes/Pilula'
import { DIAS_DA_SEMANA as DIAS_CURTOS, faixaDeHorarios } from '../../dominio/entrega'
import {
  CAPACIDADE_TETO, DIAS_DA_SEMANA, camposDaJanela, corpoJanela, corposJanela, erroDoFormularioJanela,
} from '../../dominio/entregasApi'
import { plural } from '../../dominio/formato'
import css from './entregasApi.module.css'
import layout from './cadastroEntrega.module.css'

// Janelas de entrega da loja (S45, issue #1440): criar marcando os dias de uma vez, editar,
// pausar, reativar e excluir. Mesma seção em Entregas › Janelas e frete e em Gestão › Janelas
// de entrega (as duas montam `CadastroEntregaApi`). Agrupada pelo dia da semana, na ordem da
// semana que começa na segunda, que é como a loja pensa a agenda.
const ORDEM_DA_SEMANA = [1, 2, 3, 4, 5, 6, 0]
const JANELA_NOVA = { dias: [], horaInicio: '12:00', horaFim: '14:00', capacidadeMaxima: '4', label: '' }

function Dias({ dias, umSo, aoMudar, ocupado }) {
  const alternar = (valor) => {
    if (umSo) { aoMudar([valor]); return }
    aoMudar(dias.includes(valor) ? dias.filter((d) => d !== valor) : [...dias, valor])
  }
  return (
    <fieldset className={css.dias}>
      <legend className="sr">Dias da semana</legend>
      {ORDEM_DA_SEMANA.map((valor) => (
        <Chip key={valor} papel="filtro" reservarIcone ativo={dias.includes(valor)} disabled={ocupado} onClick={() => alternar(valor)}>
          {DIAS_CURTOS.find((d) => d.valor === valor).rotulo}
        </Chip>
      ))}
    </fieldset>
  )
}

// Um formulário só para criar e editar: os dois nunca divergem na validação. Editar mexe numa
// janela, então o dia é um só; criar aceita vários e sai uma janela por dia.
// #1776 (item 20): `janelas` e `propria` deixam o formulário recusar o horário que cruza com outro.
function FormularioJanela({ inicial, editando, janelas, propria = null, aoSalvar, aoCancelar, ocupado }) {
  const [f, setF] = useState(inicial)
  const [erro, setErro] = useState(null)
  const formularioRef = useRef(null)
  useEffect(() => { formularioRef.current?.querySelector('button')?.focus() }, [])
  const campo = (k) => ({ value: f[k], disabled: ocupado, onChange: (e) => setF({ ...f, [k]: e.target.value }) })
  const salvar = (e) => {
    e.preventDefault()
    const motivo = erroDoFormularioJanela(f, janelas, propria)
    if (motivo) { setErro(motivo); return }
    setErro(null)
    aoSalvar(f).then((ok) => { if (ok && !editando) setF(inicial) })
  }
  const faixa = faixaDeHorarios(f.horaInicio, f.horaFim)
  return (
    <form ref={formularioRef} className={`${css.edicaoJanela} ${layout.edicao}`} onSubmit={salvar}>
      <Dias dias={f.dias} umSo={editando} ocupado={ocupado} aoMudar={(dias) => setF({ ...f, dias })} />
      <div className={`${css.formulario} ${layout.formularioJanela}`}>
        <CampoTexto rotulo="Início" tipo="time" {...campo('horaInicio')} required />
        <CampoTexto rotulo="Fim" tipo="time" {...campo('horaFim')} required />
        <CampoTexto rotulo="Vagas (pedidos)" tipo="number" min="1" max={CAPACIDADE_TETO} step="1" {...campo('capacidadeMaxima')} required />
        <CampoTexto rotulo="Nome" placeholder={faixa || 'Almoço'} {...campo('label')} />
      </div>
      {erro && <p className={css.erroCampo} role="alert">{erro}</p>}
      <div className={css.linha}>
        <Botao tipo="submit" variante="primario" disabled={ocupado}>
          {editando ? 'Salvar' : f.dias.length > 1 ? `Criar ${f.dias.length} janelas` : 'Criar janela'}
        </Botao>
        {aoCancelar && <Botao variante="texto" disabled={ocupado} onClick={aoCancelar}>Cancelar</Botao>}
      </div>
    </form>
  )
}

function LinhaJanela({ janela, janelas, acoes, ocupado, aoExcluir, focoAlternativo }) {
  const [modo, setModo] = useState(null) // null | 'editar' | 'excluir'
  const editarRef = useRef(null)
  const fechar = () => {
    setModo(null)
    // Trocar o dia desmonta a linha no grupo antigo; o cadastro conserva a saída por teclado.
    requestAnimationFrame(() => (editarRef.current ?? focoAlternativo.current)?.focus())
  }
  const faixa = faixaDeHorarios(String(janela.horaInicio).slice(0, 5), String(janela.horaFim).slice(0, 5))
  const mostraNome = janela.label && janela.label !== faixa

  if (modo === 'editar') {
    return (
      <li className={css.cartao}>
        <FormularioJanela
          inicial={camposDaJanela(janela)}
          editando
          janelas={janelas}
          propria={janela.id}
          ocupado={ocupado}
          aoSalvar={(f) => acoes.atualizarJanela(janela.id, corpoJanela({ ...f, diaDaSemana: f.dias[0], label: f.label || faixaDeHorarios(f.horaInicio, f.horaFim) }))
            .then((ok) => { if (ok) fechar() })}
          aoCancelar={fechar}
        />
      </li>
    )
  }
  return (
    <li className={`${css.cartao} ${layout.janela}`}>
      <div className={layout.identidade}>
        <strong className={layout.horario}>{faixa}</strong>
        {mostraNome && <span className={css.apoio}>{janela.label}</span>}
      </div>
      <div className={layout.capacidade}>
        <strong>{plural(janela.capacidadeMaxima, 'vaga', 'vagas')}</strong>
        <Pilula tom={janela.ativa ? 'ok' : 'neutro'}>{janela.ativa ? 'Ativa' : 'Pausada'}</Pilula>
      </div>
      {modo === 'excluir' ? (
        <div className={`${css.linha} ${layout.confirmacao}`}>
          <span className={`${css.apoio} ${css.cresce}`}>Excluir esta janela? Se ela já tiver pedidos, use Pausar para preservar o histórico.</span>
          <Botao variante="secundario" disabled={ocupado} onClick={aoExcluir}>Excluir janela</Botao>
          <Botao variante="texto" disabled={ocupado} onClick={fechar}>Manter janela</Botao>
        </div>
      ) : (
        <div className={`${css.linha} ${layout.acoes}`}>
          <Botao ref={editarRef} variante="texto" icone="lapis" disabled={ocupado} aria-label={`Editar janela ${faixa}`} onClick={() => setModo('editar')}>Editar</Botao>
          <Botao variante="texto" disabled={ocupado} icone={janela.ativa ? 'pause' : 'play'} onClick={() => acoes.alternarJanela(janela)}>
            {janela.ativa ? 'Pausar' : 'Reativar'}
          </Botao>
          <Botao variante="texto" icone="x" disabled={ocupado} aria-label={`Excluir janela ${faixa}`} onClick={() => setModo('excluir')}>Excluir</Botao>
        </div>
      )}
    </li>
  )
}

export function JanelasDaLoja({ janelas, acoes, ocupado }) {
  const [criando, setCriando] = useState(false)
  const criarRef = useRef(null)
  const lista = janelas ?? []
  const dias = ORDEM_DA_SEMANA.filter((d) => lista.some((j) => j.diaDaSemana === d))

  return (
    <section className={css.secao} aria-label="Janelas de entrega">
      <div className={layout.cabecalho}>
        <h2>Horários da semana</h2>
        <Botao ref={criarRef} variante={criando ? 'texto' : 'primario'} disabled={ocupado} icone={criando ? undefined : 'plus'} aria-expanded={criando} onClick={() => setCriando((v) => !v)}>
          {criando ? 'Cancelar' : 'Nova janela'}
        </Botao>
      </div>
      <p className={css.apoio}>
        Horário e capacidade por dia. Fechamentos pontuais ficam em Bloqueios.
      </p>
      {criando && (
        <div className={css.cartao}>
          <FormularioJanela inicial={JANELA_NOVA} janelas={lista} ocupado={ocupado} aoSalvar={(f) => acoes.criarJanelas(corposJanela(f)).then((ok) => {
            if (ok) { setCriando(false); requestAnimationFrame(() => criarRef.current?.focus()) }
            return ok
          })} />
        </div>
      )}
      {lista.length === 0 && !criando && <p className={css.vazio}>Nenhuma janela cadastrada. Sem janela, o cliente não tem horário para escolher.</p>}
      {dias.map((d) => (
        <div key={d} className={css.diaDaSemana}>
          <h3 className={layout.dia}>{DIAS_DA_SEMANA[d]}</h3>
          <ul className={css.lista}>
            {lista
              .filter((j) => j.diaDaSemana === d)
              .sort((a, b) => String(a.horaInicio).localeCompare(String(b.horaInicio)))
              .map((j) => <LinhaJanela key={j.id} janela={j} janelas={lista} acoes={acoes} ocupado={ocupado} focoAlternativo={criarRef} aoExcluir={() => acoes.excluirJanela(j.id).then((ok) => {
                if (ok) requestAnimationFrame(() => criarRef.current?.focus())
              })} />)}
          </ul>
        </div>
      ))}
    </section>
  )
}
