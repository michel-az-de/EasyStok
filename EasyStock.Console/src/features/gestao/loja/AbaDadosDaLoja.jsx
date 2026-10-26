// Dados da loja (#2070, só no modo API): um cartão por loja ativa com nome, descrição, documento,
// endereço e telefone. O PUT sobrescreve os cinco, então o cartão só salva com os cinco lidos.
import { useCallback, useId, useState } from 'react'
import { carregarLojasAtivas, salvarDadosDaLoja } from '../../../aplicacao/dadosDaLoja'
import { useEnvio, useLeitura, usePendenciaAvisada } from '../../../aplicacao/formularioDeAjuste'
import { Botao } from '../../../componentes/Botao'
import { CampoArea, CampoTexto } from '../../../componentes/Campo'
import { Vazio } from '../../../componentes/Vazio'
import { LIMITES_DA_LOJA, errosDaLoja, formularioDaLoja, lojaMudou, lojaTemOsCincoCampos } from '../../../dominio/dadosDaLoja'
import { mascaraTelefone } from '../../../dominio/formato'
import { Erros, FalhaDeLeitura, Pendencia, Retorno } from '../PartesDoAjuste'
import css from '../formularioDeAjuste.module.css'

const ROTULOS = { nome: 'Nome da loja', descricao: 'Descrição', documento: 'CNPJ ou CPF', endereco: 'Endereço', telefone: 'Telefone' }

export function AbaDadosDaLoja({ aoMudarPendencia, aoSalvo, compacto = false }) {
  const [carga, recarregar] = useLeitura(carregarLojasAtivas)
  const [pendentes, setPendentes] = useState({})
  usePendenciaAvisada(Object.values(pendentes).some(Boolean), aoMudarPendencia)
  const marcar = useCallback((id, pendente) => setPendentes((atuais) => (Boolean(atuais[id]) === pendente ? atuais : { ...atuais, [id]: pendente })), [])

  return (
    <div className={css.aba}>
      {!compacto && <p className={css.descricao}>O cadastro da loja dentro do EasyStok. Cada loja salva no próprio botão.</p>}
      {carga.estado === 'carregando' && <output className={css.descricao}>Carregando os dados da loja…</output>}
      {carga.estado === 'erro' && <FalhaDeLeitura mensagem={`Os dados da loja não carregaram: ${carga.mensagem}`} aoTentarDeNovo={recarregar} />}
      {carga.estado === 'ok' && carga.dados.length === 0 && (
        <Vazio titulo="Nenhuma loja ativa">Peça ao suporte para cadastrar a loja desta empresa.</Vazio>
      )}
      {carga.estado === 'ok' && carga.dados.map((loja) => (
        <CartaoDaLoja key={loja.id} loja={loja} aoMudarPendencia={(pendente) => marcar(loja.id, pendente)} aoSalvo={aoSalvo} />
      ))}
    </div>
  )
}

function CartaoDaLoja({ loja: lida, aoMudarPendencia, aoSalvo }) {
  const id = useId()
  const [loja, setLoja] = useState(lida)
  const [form, setForm] = useState(() => formularioDaLoja(lida))
  const [erros, setErros] = useState({})
  const [situacao, enviar, limpar] = useEnvio()
  const completa = lojaTemOsCincoCampos(loja)
  const pendente = lojaMudou(form, loja)
  const salvando = situacao.estado === 'salvando'
  usePendenciaAvisada(pendente, aoMudarPendencia)

  const mudar = (campo, ajustar = (valor) => valor) => (evento) => {
    const valor = ajustar(evento.target.value)
    limpar()
    setErros({})
    setForm((atual) => ({ ...atual, [campo]: valor }))
  }

  async function salvar(evento) {
    evento.preventDefault()
    if (!completa || !pendente || salvando) return
    const encontrados = errosDaLoja(form)
    setErros(encontrados)
    if (Object.keys(encontrados).length > 0) return
    const { ok, resultado } = await enviar(() => salvarDadosDaLoja(loja, form))
    if (!ok) return
    setLoja(resultado)
    setForm(formularioDaLoja(resultado))
    aoSalvo?.()
  }

  function desfazer() {
    setForm(formularioDaLoja(loja))
    setErros({})
    limpar()
  }

  return (
    <form className={css.cartao} aria-labelledby={`${id}-titulo`} onSubmit={salvar}>
      <h3 id={`${id}-titulo`}>{loja.nome || 'Loja'}</h3>
      {!completa && (
        <p className={css.aviso}>
          Esta versão do EasyStok ainda não devolve a descrição, o documento, o endereço e o telefone da loja.
          Salvar agora apagaria o que já está gravado, então o cartão fica só para leitura até a atualização.
        </p>
      )}
      <Pendencia visivel={pendente && !salvando} />
      <fieldset className={css.campos} disabled={!completa || salvando}>
        <legend className="sr">Dados de {loja.nome || 'a loja'}</legend>
        <CampoTexto rotulo={ROTULOS.nome} required maxLength={LIMITES_DA_LOJA.nome} value={form.nome} onChange={mudar('nome')} />
        <CampoTexto rotulo={ROTULOS.documento} maxLength={LIMITES_DA_LOJA.documento} inputMode="numeric" value={form.documento} onChange={mudar('documento')} />
        <CampoTexto rotulo={ROTULOS.telefone} tipo="tel" inputMode="tel" maxLength={LIMITES_DA_LOJA.telefone} value={form.telefone} onChange={mudar('telefone', mascaraTelefone)} />
        <div className={css.inteiro}>
          <CampoTexto rotulo={ROTULOS.endereco} maxLength={LIMITES_DA_LOJA.endereco} autoComplete="street-address" value={form.endereco} onChange={mudar('endereco')} />
        </div>
        <div className={css.inteiro}>
          <CampoArea rotulo={ROTULOS.descricao} maxLength={LIMITES_DA_LOJA.descricao} value={form.descricao} onChange={mudar('descricao')} />
        </div>
      </fieldset>
      <Erros erros={erros} rotulos={ROTULOS} />
      <div className={css.acoes}>
        <Botao variante="primario" tipo="submit" className={css.toque} disabled={!completa || !pendente || salvando}>Salvar dados da loja</Botao>
        <Botao variante="texto" className={css.toque} disabled={!pendente || salvando} onClick={desfazer}>Desfazer edição</Botao>
        <Retorno situacao={situacao} />
      </div>
    </form>
  )
}
