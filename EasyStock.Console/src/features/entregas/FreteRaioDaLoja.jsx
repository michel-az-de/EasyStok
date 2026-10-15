import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoTexto } from '../../componentes/Campo'
import {
  corpoFreteRaio, corpoTesteDeFrete, erroDoFormularioFreteRaio, formularioDoFreteRaio, resumoDoTesteDeFrete,
} from '../../dominio/entregasApi'
import css from './entregasApi.module.css'
import layout from './cadastroEntrega.module.css'

// Frete por distância da loja (I2.2, #1651): cozinha, raio e faixas em `api/minha-vitrine/entrega/raio`,
// e o "Testar um endereço", que devolve a mesma conta do checkout. A API guarda só a coordenada da
// cozinha: com ela gravada, CEP e número em branco mantêm a cozinha atual.
const TESTE_VAZIO = { cep: '', numero: '' }

export function FreteRaioDaLoja({ config, acoes, ocupado }) {
  // Enquanto ninguém edita, o formulário espelha o que está gravado (e acompanha a recarga).
  const [edicao, setEdicao] = useState(null)
  const [erro, setErro] = useState(null)
  const [teste, setTeste] = useState(TESTE_VAZIO)
  const [testando, setTestando] = useState(false)
  const [resultado, setResultado] = useState(null)

  const configurado = Boolean(config?.configurado)
  const form = edicao ?? formularioDoFreteRaio(config)
  const mudar = (parte) => { setEdicao({ ...form, ...parte }); setErro(null) }
  const campo = (k) => ({ value: form[k], disabled: ocupado, onChange: (e) => mudar({ [k]: e.target.value }) })
  const mudarFaixa = (indice, parte) => mudar({ faixas: form.faixas.map((x, i) => (i === indice ? { ...x, ...parte } : x)) })
  const campoTeste = (k) => ({ value: teste[k], disabled: testando, onChange: (e) => setTeste({ ...teste, [k]: e.target.value }) })

  const salvar = (e) => {
    e.preventDefault()
    const motivo = erroDoFormularioFreteRaio(form, configurado)
    if (motivo) { setErro(motivo); return }
    acoes.salvarFreteRaio(corpoFreteRaio(form)).then((ok) => { if (ok) setEdicao(null) })
  }

  const testar = (e) => {
    e.preventDefault()
    setTestando(true)
    acoes.testarFreteRaio(corpoTesteDeFrete(teste))
      .then((r) => setResultado(resumoDoTesteDeFrete(r)))
      .catch((falha) => setResultado({ tom: 'erro', titulo: 'O teste não rodou', detalhe: falha.message }))
      .finally(() => setTestando(false))
  }

  return (
    <div className={layout.freteRaio}>
      <div className={layout.cabecalho}>
        <div>
          <h2>Frete por distância</h2>
          <p className={css.apoio}>
            {configurado
              ? 'Cozinha localizada. O frete sai da distância até o cliente; a tabela de zonas cobre o endereço que não for localizado.'
              : 'Ainda não configurado: hoje o frete sai só da tabela de zonas.'}
          </p>
        </div>
      </div>

      <form className={css.secao} onSubmit={salvar} noValidate>
        <fieldset>
          <legend>Cozinha</legend>
          <div className={css.formulario}>
            <CampoTexto rotulo="CEP da cozinha" inputMode="numeric" autoComplete="off" {...campo('cep')}
              dica={configurado ? 'Em branco, mantém a cozinha atual.' : undefined} />
            <CampoTexto rotulo="Número" autoComplete="off" {...campo('numero')} />
          </div>
        </fieldset>
        <fieldset>
          <legend>Raio</legend>
          <div className={css.formulario}>
            <CampoTexto rotulo="Raio máximo (km)" inputMode="decimal" {...campo('raioKm')} />
            <CampoTexto rotulo="Frete grátis até (km)" inputMode="decimal" {...campo('gratisKm')} />
            <CampoTexto rotulo="Fator de rota" inputMode="decimal" {...campo('fatorRota')}
              dica="Quanto a rua alonga a linha reta. Usado quando a rota não é medida." />
          </div>
        </fieldset>
        <fieldset>
          <legend>Faixas de preço</legend>
          {form.faixas.map((faixa, indice) => (
            <div key={indice} className={layout.faixaDeFrete}>
              <CampoTexto rotulo="Até (km)" inputMode="decimal" value={faixa.ateKm} disabled={ocupado}
                onChange={(e) => mudarFaixa(indice, { ateKm: e.target.value })} />
              <CampoTexto rotulo="Valor (R$)" inputMode="decimal" value={faixa.valor} disabled={ocupado}
                onChange={(e) => mudarFaixa(indice, { valor: e.target.value })} />
              <Botao variante="texto" disabled={ocupado || form.faixas.length === 1} aria-label={`Remover faixa ${indice + 1}`}
                onClick={() => mudar({ faixas: form.faixas.filter((_, i) => i !== indice) })}>Remover</Botao>
            </div>
          ))}
          <div className={layout.acoesFormulario}>
            <Botao variante="texto" icone="plus" disabled={ocupado} onClick={() => mudar({ faixas: [...form.faixas, { ateKm: '', valor: '' }] })}>Adicionar faixa</Botao>
          </div>
        </fieldset>
        {erro && <p className={css.erroCampo} role="alert">{erro}</p>}
        <div className={layout.acoesFormulario}>
          <Botao tipo="submit" variante="primario" disabled={ocupado}>Salvar frete por distância</Botao>
          {edicao && <Botao variante="texto" disabled={ocupado} onClick={() => { setEdicao(null); setErro(null) }}>Descartar alterações</Botao>}
        </div>
      </form>

      <form className={css.secao} onSubmit={testar}>
        <h3>Testar um endereço</h3>
        <div className={css.formulario}>
          <CampoTexto rotulo="CEP do cliente" inputMode="numeric" autoComplete="off" {...campoTeste('cep')} required />
          <CampoTexto rotulo="Número" autoComplete="off" {...campoTeste('numero')} required />
          <Botao tipo="submit" variante="secundario" disabled={testando}>{testando ? 'Testando…' : 'Testar endereço'}</Botao>
        </div>
        <div aria-live="polite">
          {resultado && (
            <div className={layout.resultadoTeste}>
              <strong className={resultado.tom === 'erro' ? css.erroCampo : undefined}>{resultado.titulo}</strong>
              {resultado.detalhe && <span className={css.apoio}>{resultado.detalhe}</span>}
            </div>
          )}
        </div>
      </form>
    </div>
  )
}
