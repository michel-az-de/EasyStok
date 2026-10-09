import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { useCadastroEntregaApi } from '../../aplicacao/useCadastroEntregaApi'
import { faixaDeHorarios } from '../../dominio/entrega'
import { DIAS_DA_SEMANA, corpoBloqueio, corpoZona } from '../../dominio/entregasApi'
import { dataCurtaComSemana, faixaDeCep, moeda } from '../../dominio/formato'
import { JanelasDaLoja } from './JanelasDaLoja'
import css from './entregasApi.module.css'

// Cadastro de entrega da loja (S45, `api/minha-vitrine/entrega`): janelas com
// capacidade, zonas de frete por CEP ou bairros e bloqueios de dia ou janela. Montado em
// Entregas › Janelas e frete e, pelo App, em Gestão › Janelas de entrega (#1440).
const rotuloDaJanela = (x) =>
  `${DIAS_DA_SEMANA[x.diaDaSemana]} ${faixaDeHorarios(String(x.horaInicio).slice(0, 5), String(x.horaFim).slice(0, 5))}`
const ZONA_VAZIA = { label: '', valor: '', tempoEstimadoMinutos: '40', ordem: '1', cobertura: 'bairros', cepInicio: '', cepFim: '', bairros: '' }
const BLOQUEIO_VAZIO = { data: '', motivo: '', janelaEspecificaId: '' }

function useFormulario(inicial) {
  const [form, setForm] = useState(inicial)
  const campo = (k) => ({ value: form[k], onChange: (e) => setForm({ ...form, [k]: e.target.value }) })
  return { form, campo, limpar: () => setForm(inicial) }
}

export function CadastroEntregaApi() {
  const { janelas, zonas, bloqueios, erro, enviando, acoes, limparErro } = useCadastroEntregaApi()
  const z = useFormulario(ZONA_VAZIA)
  const b = useFormulario(BLOQUEIO_VAZIO)
  const enviar = (acao, f, corpo) => (e) => {
    e.preventDefault()
    acao(corpo(f.form)).then((ok) => { if (ok) f.limpar() })
  }

  if (janelas === null && !erro) return <p className={css.vazio}>Carregando o cadastro…</p>
  const rotuloJanelaPorId = (id) => {
    const janela = (janelas ?? []).find((w) => w.id === id)
    return janela ? rotuloDaJanela(janela) : 'Janela excluída'
  }
  const opcoesJanela = [{ valor: '', rotulo: 'O dia inteiro' }, ...(janelas ?? []).map((x) => ({ valor: x.id, rotulo: rotuloDaJanela(x) }))]

  return (
    <div className={css.cadastro}>
      {erro && (
        <p className={`${css.faixa} ${css.faixaFixa}`} role="alert">
          <Icone nome="circle-x" tamanho={16} /> <span className={css.cresce}>{erro}</span>
          <Botao variante="texto" onClick={limparErro}>Fechar</Botao>
        </p>
      )}
      <JanelasDaLoja janelas={janelas} acoes={acoes} ocupado={enviando} />

      <section className={css.secao} aria-label="Zonas de frete">
        <h3>Zonas de frete</h3>
        <form className={css.formulario} onSubmit={enviar(acoes.criarZona, z, corpoZona)}>
          <CampoTexto rotulo="Nome" {...z.campo('label')} required />
          <CampoTexto rotulo="Valor (R$)" inputMode="decimal" {...z.campo('valor')} required />
          <CampoTexto rotulo="Tempo (min)" tipo="number" min="1" {...z.campo('tempoEstimadoMinutos')} required />
          <CampoTexto rotulo="Ordem" tipo="number" min="0" {...z.campo('ordem')} />
          <CampoSelecao
            rotulo="Cobertura" opcoes={[{ valor: 'bairros', rotulo: 'Bairros' }, { valor: 'cep', rotulo: 'Faixa de CEP' }]}
            {...z.campo('cobertura')}
          />
          {z.form.cobertura === 'cep'
            ? (
              <>
                <CampoTexto rotulo="CEP inicial" inputMode="numeric" {...z.campo('cepInicio')} required />
                <CampoTexto rotulo="CEP final" inputMode="numeric" {...z.campo('cepFim')} required />
              </>
            )
            : <CampoTexto rotulo="Bairros (separados por vírgula)" {...z.campo('bairros')} required />}
          <Botao tipo="submit" variante="primario" disabled={enviando}>Criar zona</Botao>
        </form>
        <ul className={css.lista}>
          {zonas.map((x) => (
            <li key={x.id} className={`${css.cartao} ${css.linha}`}>
              <span className={css.cresce}>{x.label} · {moeda(Number(x.valor) || 0)} · {x.tempoEstimadoMinutos} min</span>
              <span className={css.apoio}>
                {x.cepInicio ? `CEP ${faixaDeCep(x.cepInicio, x.cepFim)}` : x.bairros.join(', ')}{x.ativa ? '' : ' · desativada'}
              </span>
              <Botao variante="texto" onClick={() => acoes.alternarZona(x)}>{x.ativa ? 'Desativar' : 'Ativar'}</Botao>
            </li>
          ))}
        </ul>
      </section>

      <section className={css.secao} aria-label="Bloqueios">
        <h3>Bloqueios (próximos 60 dias)</h3>
        <form className={css.formulario} onSubmit={enviar(acoes.criarBloqueio, b, corpoBloqueio)}>
          <CampoTexto rotulo="Data" tipo="date" {...b.campo('data')} required />
          <CampoSelecao rotulo="Janela" opcoes={opcoesJanela} {...b.campo('janelaEspecificaId')} />
          <CampoTexto rotulo="Motivo" {...b.campo('motivo')} required />
          <Botao tipo="submit" variante="primario" disabled={enviando}>Bloquear</Botao>
        </form>
        <ul className={css.lista}>
          {bloqueios.map((x) => (
            <li key={x.id} className={`${css.cartao} ${css.linha}`}>
              <span className={css.cresce}>{dataCurtaComSemana(x.data)} · {x.motivo}</span>
              <span className={css.apoio}>{x.janelaEspecificaId ? rotuloJanelaPorId(x.janelaEspecificaId) : 'Dia inteiro'}</span>
              <Botao variante="texto" onClick={() => acoes.removerBloqueio(x.id)}>Remover</Botao>
            </li>
          ))}
        </ul>
      </section>
    </div>
  )
}
