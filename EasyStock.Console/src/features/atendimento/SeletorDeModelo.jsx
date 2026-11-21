import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { algumPassaDoLimite } from '../../componentes/limiteDoCampo'
import { useAcoes } from '../../aplicacao/contextos'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { podeVerIntegracoes } from '../../dominio/acessoModulos'
import { erroDoModelo } from '../../dominio/falhaDeEnvio'
import { ROTULOS } from '../../dominio/rotulos'
import css from './atendimento.module.css'

// #2792: o limite de cada campo do modelo espelha o servidor; sem `maxLength`, o texto colado fica inteiro e o envio trava.
const LIMITE_PARAMETRO = 1024

// #2783: o seletor de modelo aprovado é um só, do Enviar modelo e do Programar mensagem. Lista os modelos da conta,
// pede os campos numerados e mostra a prévia; quem usa recebe `{ modelo, parametros, falta, sujo }` em `aoMudar`
// e a falha de carregar (ou null) em `aoFalhar`, para escrevê-la onde já escreve os outros erros.
export function SeletorDeModelo({ desabilitado = false, aoMudar, aoFalhar }) {
  const { listarModelos } = useAcoes()
  const { acoes } = useAcessoModulos()
  // #2007 (11c): sem acesso a Integrações, a frase manda pedir à gerência em vez de apontar a tela que ela não vê.
  const podeIntegrar = podeVerIntegracoes(acoes)
  const [lista, setLista] = useState(null)
  const [falhou, setFalhou] = useState(false)
  const [escolha, setEscolha] = useState('')
  const [parametros, setParametros] = useState([])
  const consulta = useRef(0)

  async function carregar(atualizar = false) {
    const versao = ++consulta.current
    setLista(null); setFalhou(false); aoFalhar?.(null); setEscolha(''); setParametros([])
    try {
      const modelos = await listarModelos(atualizar)
      if (versao === consulta.current) setLista(modelos)
    } catch (e) {
      if (versao === consulta.current) { setLista([]); setFalhou(true); aoFalhar?.(erroDoModelo(e, { podeIntegrar })) }
    }
  }
  useEffect(() => {
    carregar()
    return () => { consulta.current++ }
    // A modal é remontada ao trocar a conversa; atualizar a lista é uma ação explícita.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const modelo = lista?.[Number(escolha)]
  const falta = escolha === '' || !modelo || Boolean(modelo.indisponivel) || parametros.some((p) => !p.trim() || p.includes('{{'))
    || algumPassaDoLimite(...parametros.map((p) => [p, LIMITE_PARAMETRO]))
  const sujo = escolha !== '' || parametros.some((p) => p.trim())
  useEffect(() => {
    aoMudar?.({ modelo: modelo ?? null, parametros: parametros.map((p) => p.trim()), falta, sujo })
    // `aoMudar` troca a cada render do pai; só a escolha e os campos o chamam de novo.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [escolha, parametros, lista])

  const previa = modelo ? [modelo.cabecalho, modelo.corpo.replace(/\{\{([1-9][0-9]?)\}\}/g,
    (original, n) => parametros[Number(n) - 1]?.trim() || original), modelo.rodape].filter(Boolean).join('\n') : ''

  return (
    <>
      <Botao onClick={() => carregar(true)} disabled={desabilitado}>Atualizar modelos</Botao>
      {lista === null && <output>Carregando as opções de {ROTULOS.MENSAGEM_PADRAO}…</output>}
      {lista?.length === 0 && !falhou && <p data-vazio>Nenhuma {ROTULOS.MENSAGEM_PADRAO} nesta conta. Crie e aprove o modelo no Gerenciador da Meta.</p>}
      {Boolean(lista?.length) && (
        <fieldset className={css.programarCampos} disabled={desabilitado}>
          <CampoSelecao rotulo={ROTULOS.MENSAGEM_PADRAO_INICIAL} value={escolha} opcoes={[
            { valor: '', rotulo: 'Selecione um modelo' },
            ...lista.map((m, i) => ({ valor: String(i), rotulo: `${m.nome} · ${m.idioma}${m.indisponivel ? ' · indisponível aqui' : ''}` })),
          ]} onChange={(e) => {
            setEscolha(e.target.value)
            setParametros(Array(lista[Number(e.target.value)]?.quantidadeParametros ?? 0).fill(''))
          }} />
          {escolha !== '' && modelo && <>
            <p>{modelo.categoria === 'MARKETING' ? 'Novidades e promoções: exige autorização de marketing na Ficha.' : 'Aviso transacional: respeita os consentimentos da Ficha.'}</p>
            {modelo.indisponivel && <p role="alert" className={css.programarErro}>{modelo.indisponivel}</p>}
            {!modelo.indisponivel && parametros.length > 0 && <p>Preencha os campos numerados e confira cada substituição na prévia abaixo.</p>}
            {!modelo.indisponivel && parametros.map((p, i) => <CampoTexto key={i} rotulo={`Campo ${i + 1} ({{${i + 1}}})`} value={p} limite={LIMITE_PARAMETRO}
              onChange={(e) => setParametros((atuais) => atuais.map((v, n) => n === i ? e.target.value : v))} />)}
            <p className={css.cardapioRotulo}>Prévia da mensagem</p>
            <p className={css.programadaTexto}>{previa}</p>
          </>}
        </fieldset>
      )}
    </>
  )
}
