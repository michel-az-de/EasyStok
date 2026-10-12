// Aba Impressão das Configurações (#1632, só no modo API): o logo da loja, que a API põe no
// topo dos impressos, e as escolhas DESTE aparelho (papel padrão de cada documento e logo na
// térmica), guardadas no navegador porque cada aparelho tem a sua impressora.
import { useEffect, useId, useRef, useState } from 'react'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import {
  enviarLogoDaLoja, gravarPreferenciasImpressao, lerPreferenciasImpressao, obterLojaDaEmpresa,
} from '../../../aplicacao/impressos'
import { Botao } from '../../../componentes/Botao'
import { CampoSelecao } from '../../../componentes/Campo'
import { DOCUMENTOS, PAPEIS, trocarLogoNaTermica, trocarPapelPadrao } from '../../../dominio/impressos'
import css from './abaImpressao.module.css'

const TIPOS_DE_LOGO = ['image/png', 'image/jpeg']
const TAMANHO_MAXIMO = 5 * 1024 * 1024

function LogoDaLoja() {
  const { acoes } = useAcessoModulos()
  const id = useId()
  const arquivoRef = useRef(null)
  const [loja, setLoja] = useState(null)
  const [situacao, setSituacao] = useState({ estado: 'carregando' })

  useEffect(() => {
    let ativo = true
    obterLojaDaEmpresa()
      .then((dados) => { if (ativo) { setLoja(dados); setSituacao({ estado: 'ocioso' }) } })
      .catch((erro) => { if (ativo) setSituacao({ estado: 'erro', mensagem: `A loja não carregou: ${erro.message}` }) })
    return () => { ativo = false }
  }, [])

  async function enviar(evento) {
    const arquivo = evento.target.files?.[0]
    evento.target.value = ''
    if (!arquivo || !loja) return
    if (!TIPOS_DE_LOGO.includes(arquivo.type)) {
      setSituacao({ estado: 'erro', mensagem: 'Escolha uma imagem PNG ou JPG.' })
      return
    }
    if (arquivo.size > TAMANHO_MAXIMO) {
      setSituacao({ estado: 'erro', mensagem: 'A imagem não pode ser maior que 5 MB.' })
      return
    }
    setSituacao({ estado: 'enviando' })
    try {
      const logoUrl = await enviarLogoDaLoja(loja.id, arquivo)
      setLoja((atual) => ({ ...atual, logoUrl }))
      setSituacao({ estado: 'enviado' })
    } catch (erro) {
      setSituacao({ estado: 'erro', mensagem: `O logo não foi enviado: ${erro.message}` })
    }
  }

  const podeEnviar = acoes.editarAtendimento === true
  const enviando = situacao.estado === 'enviando'
  return (
    <section className={css.secao} aria-labelledby={`${id}-logo`}>
      <h3 id={`${id}-logo`}>Logo da loja</h3>
      <p className={css.descricao}>PNG ou JPG; aparece no topo dos impressos. Na térmica só sai em preto.</p>
      <div className={css.logo}>
        {loja?.logoUrl
          ? <img className={css.previa} src={loja.logoUrl} alt={`Logo de ${loja.nome || 'a loja'}`} />
          : <p className={css.semPrevia}>{situacao.estado === 'carregando' ? 'Carregando a loja…' : 'O logo aparece aqui depois do envio.'}</p>}
        {podeEnviar ? (
          <div className={css.acoes}>
            <input
              ref={arquivoRef} type="file" accept={TIPOS_DE_LOGO.join(',')} className="sr"
              aria-label="Arquivo do logo da loja" tabIndex={-1} onChange={enviar}
            />
            <Botao icone="imagem" className={css.toque} disabled={!loja || enviando} onClick={() => arquivoRef.current?.click()}>
              {enviando ? 'Enviando…' : loja?.logoUrl ? 'Trocar logo' : 'Enviar logo'}
            </Botao>
          </div>
        ) : <p className={css.descricao}>O logo é enviado pela dona.</p>}
      </div>
      {situacao.estado === 'enviado' && <output className={css.retorno}>Logo salvo no EasyStok. Os próximos impressos já saem com ele.</output>}
      {situacao.estado === 'erro' && <p className={css.erro} role="alert">{situacao.mensagem}</p>}
    </section>
  )
}

function PapeisDoAparelho() {
  const id = useId()
  const [preferencias, setPreferencias] = useState(lerPreferenciasImpressao)
  // null: nada mudou ainda. true/false: se o navegador guardou a última troca.
  const [guardou, setGuardou] = useState(null)
  const aplicar = (proximas) => {
    setPreferencias(proximas)
    setGuardou(gravarPreferenciasImpressao(proximas))
  }

  return (
    <section className={css.secao} aria-labelledby={`${id}-papeis`}>
      <h3 id={`${id}-papeis`}>Papel padrão neste aparelho</h3>
      <p className={css.descricao}>
        Cada aparelho guarda o seu: o tablet da cozinha pode sair na bobina e o computador do balcão na A4.
        Na hora de imprimir ainda dá para trocar.
      </p>
      <div className={css.papeis}>
        {DOCUMENTOS.map((documento) => (
          <CampoSelecao
            key={documento.valor}
            rotulo={documento.rotulo}
            opcoes={PAPEIS}
            value={preferencias.papeis[documento.valor]}
            onChange={(evento) => aplicar(trocarPapelPadrao(preferencias, documento.valor, evento.target.value))}
          />
        ))}
      </div>
      <label className={css.chave}>
        <input
          type="checkbox"
          checked={preferencias.logoTermica}
          aria-describedby={`${id}-termica`}
          onChange={(evento) => aplicar(trocarLogoNaTermica(preferencias, evento.target.checked))}
        />
        Usar o logo também na térmica
      </label>
      <p id={`${id}-termica`} className={css.aviso}>
        A térmica só imprime preto: logo colorido pode sair falhado. O padrão é o nome da casa em texto,
        que sai sempre nítido.
      </p>
      {guardou === true && <output className={css.retorno}>Guardado neste aparelho.</output>}
      {guardou === false && (
        <output className={css.retorno}>O navegador não deixou guardar. A escolha vale até fechar esta aba.</output>
      )}
    </section>
  )
}

export function AbaImpressao() {
  return (
    <div className={css.aba}>
      <LogoDaLoja />
      <PapeisDoAparelho />
    </div>
  )
}
