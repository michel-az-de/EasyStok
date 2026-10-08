import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Chip } from '../../componentes/Chip'
import { CampoTexto } from '../../componentes/Campo'
import { Pilula } from '../../componentes/Pilula'
import { DIAS_DA_SEMANA as DIAS_CURTOS, faixaDeHorarios } from '../../dominio/entrega'
import {
  DIAS_DA_SEMANA, camposDaJanela, corpoJanela, corposJanela, erroDoFormularioJanela,
} from '../../dominio/entregasApi'
import css from './entregasApi.module.css'

// Janelas de entrega da loja (S45, issue #1440): criar marcando os dias de uma vez, editar,
// pausar, reativar e excluir. Mesma seção em Entregas › Janelas e frete e em Gestão › Janelas
// de entrega (as duas montam `CadastroEntregaApi`). Agrupada pelo dia da semana, na ordem da
// semana que começa na segunda, que é como a loja pensa a agenda.
const ORDEM_DA_SEMANA = [1, 2, 3, 4, 5, 6, 0]
const JANELA_NOVA = { dias: [], horaInicio: '12:00', horaFim: '14:00', capacidadeMaxima: '4', label: '' }

function Dias({ dias, umSo, aoMudar }) {
  const alternar = (valor) => {
    if (umSo) { aoMudar([valor]); return }
    aoMudar(dias.includes(valor) ? dias.filter((d) => d !== valor) : [...dias, valor])
  }
  return (
    <fieldset className={css.dias}>
      <legend className="sr">Dias da semana</legend>
      {ORDEM_DA_SEMANA.map((valor) => (
        <Chip key={valor} papel="filtro" ativo={dias.includes(valor)} onClick={() => alternar(valor)}>
          {DIAS_CURTOS.find((d) => d.valor === valor).rotulo}
        </Chip>
      ))}
    </fieldset>
  )
}

// Um formulário só para criar e editar: os dois nunca divergem na validação. Editar mexe numa
// janela, então o dia é um só; criar aceita vários e sai uma janela por dia.
function FormularioJanela({ inicial, editando, aoSalvar, aoCancelar, ocupado }) {
  const [f, setF] = useState(inicial)
  const [erro, setErro] = useState(null)
  const campo = (k) => ({ value: f[k], onChange: (e) => setF({ ...f, [k]: e.target.value }) })
  const salvar = (e) => {
    e.preventDefault()
    const motivo = erroDoFormularioJanela(f)
    if (motivo) { setErro(motivo); return }
    setErro(null)
    aoSalvar(f).then((ok) => { if (ok && !editando) setF(inicial) })
  }
  const faixa = faixaDeHorarios(f.horaInicio, f.horaFim)
  return (
    <form className={css.edicaoJanela} onSubmit={salvar}>
      <Dias dias={f.dias} umSo={editando} aoMudar={(dias) => setF({ ...f, dias })} />
      <div className={css.formulario}>
        <CampoTexto rotulo="Início" tipo="time" {...campo('horaInicio')} required />
        <CampoTexto rotulo="Fim" tipo="time" {...campo('horaFim')} required />
        <CampoTexto rotulo="Vagas (pedidos)" tipo="number" min="1" step="1" {...campo('capacidadeMaxima')} required />
        <CampoTexto rotulo="Nome" placeholder={faixa || 'Almoço'} {...campo('label')} />
      </div>
      {erro && <p className={css.erroCampo} role="alert">{erro}</p>}
      <div className={css.linha}>
        <Botao tipo="submit" variante="primario" disabled={ocupado}>
          {editando ? 'Salvar' : f.dias.length > 1 ? `Criar ${f.dias.length} janelas` : 'Criar janela'}
        </Botao>
        {aoCancelar && <Botao variante="texto" onClick={aoCancelar}>Cancelar</Botao>}
      </div>
    </form>
  )
}

function LinhaJanela({ janela, acoes }) {
  const [modo, setModo] = useState(null) // null | 'editar' | 'excluir'
  const faixa = faixaDeHorarios(String(janela.horaInicio).slice(0, 5), String(janela.horaFim).slice(0, 5))
  const mostraNome = janela.label && janela.label !== faixa

  if (modo === 'editar') {
    return (
      <li className={css.cartao}>
        <FormularioJanela
          inicial={camposDaJanela(janela)}
          editando
          aoSalvar={(f) => acoes.atualizarJanela(janela.id, corpoJanela({ ...f, diaDaSemana: f.dias[0], label: f.label || faixaDeHorarios(f.horaInicio, f.horaFim) }))
            .then((ok) => { if (ok) setModo(null) })}
          aoCancelar={() => setModo(null)}
        />
      </li>
    )
  }
  return (
    <li className={`${css.cartao} ${janela.ativa ? '' : css.pausada}`}>
      <div className={css.linha}>
        <span className={`${css.cresce} ${css.numero}`}>
          {faixa}{mostraNome && <span className={css.nomeJanela}> · {janela.label}</span>}
        </span>
        {!janela.ativa && <Pilula tom="neutro">Pausada</Pilula>}
        <span className={css.apoio}>{janela.capacidadeMaxima} vagas</span>
      </div>
      {modo === 'excluir' ? (
        <div className={css.linha}>
          <span className={`${css.apoio} ${css.cresce}`}>Excluir de vez? Se já teve pedido, a loja recusa e o caminho é pausar.</span>
          <Botao variante="secundario" onClick={() => acoes.excluirJanela(janela.id).then(() => setModo(null))}>Excluir</Botao>
          <Botao variante="texto" onClick={() => setModo(null)}>Não</Botao>
        </div>
      ) : (
        <div className={css.linha}>
          <Botao variante="texto" icone="lapis" onClick={() => setModo('editar')}>Editar</Botao>
          <Botao variante="texto" icone={janela.ativa ? 'pause' : 'play'} onClick={() => acoes.alternarJanela(janela)}>
            {janela.ativa ? 'Pausar' : 'Reativar'}
          </Botao>
          <Botao variante="texto" icone="x" onClick={() => setModo('excluir')}>Excluir</Botao>
        </div>
      )}
    </li>
  )
}

export function JanelasDaLoja({ janelas, acoes }) {
  const [criando, setCriando] = useState(false)
  const lista = janelas ?? []
  const dias = ORDEM_DA_SEMANA.filter((d) => lista.some((j) => j.diaDaSemana === d))

  return (
    <section className={css.secao} aria-label="Janelas de entrega">
      <div className={css.linha}>
        <h3 className={css.cresce}>Janelas de entrega</h3>
        <Botao variante={criando ? 'texto' : 'primario'} icone={criando ? undefined : 'plus'} aria-expanded={criando} onClick={() => setCriando((v) => !v)}>
          {criando ? 'Cancelar' : 'Nova janela'}
        </Botao>
      </div>
      <p className={css.apoio}>
        Cada janela é um horário de entrega num dia da semana, com o número de pedidos que cabem nele.
        Para fechar um dia específico (feriado, chuva), use Bloqueios.
      </p>
      {criando && (
        <div className={css.cartao}>
          <FormularioJanela inicial={JANELA_NOVA} aoSalvar={(f) => acoes.criarJanelas(corposJanela(f))} />
        </div>
      )}
      {lista.length === 0 && !criando && <p className={css.vazio}>Nenhuma janela cadastrada. Sem janela, o cliente não tem horário para escolher.</p>}
      {dias.map((d) => (
        <div key={d} className={css.diaDaSemana}>
          <h4>{DIAS_DA_SEMANA[d]}</h4>
          <ul className={css.lista}>
            {lista
              .filter((j) => j.diaDaSemana === d)
              .sort((a, b) => String(a.horaInicio).localeCompare(String(b.horaInicio)))
              .map((j) => <LinhaJanela key={j.id} janela={j} acoes={acoes} />)}
          </ul>
        </div>
      ))}
    </section>
  )
}
