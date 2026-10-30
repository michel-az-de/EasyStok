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
import {
  DOCUMENTOS, PAPEIS, PERFIS_IMPRESSAO, orientacaoDoPapel, perfilDeImpressao, trocarCanhotoAutomatico, trocarLogoNaTermica, trocarPapelPadrao, trocarPerfilPadrao,
} from '../../../dominio/impressos'
import { atalhoDoChrome } from '../../../dominio/impressaoAutomatica'
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
        ) : <p className={css.descricao}>Peça à administração da loja para enviar o logo.</p>}
      </div>
      {situacao.estado === 'enviado' && <output className={css.retorno}>Logo salvo no EasyStok. Os próximos impressos já saem com ele.</output>}
      {situacao.estado === 'erro' && <p className={css.erro} role="alert">{situacao.mensagem}</p>}
    </section>
  )
}

// #2096: o canhoto sai sozinho no PC da cozinha. Sem --kiosk-printing o Chrome abre o diálogo a cada folha.
const enderecoDoConsole = () => `${window.location.origin}${window.location.pathname}`

function CanhotoSozinho({ ligado, aoMudar }) {
  const id = useId()
  return (
    <div className={css.automatico}>
      <label className={css.chave}>
        <input
          type="checkbox"
          role="switch"
          checked={ligado}
          aria-checked={ligado}
          aria-describedby={`${id}-automatico`}
          onChange={(evento) => aoMudar(evento.target.checked)}
        />
        Imprimir pedidos automaticamente neste aparelho
      </label>
      <p id={`${id}-automatico`} className={css.descricao}>
        Pedidos pagos são impressos enquanto o sistema estiver aberto. Só uma aba imprime.
      </p>
      {ligado && (
        <div className={css.aviso}>
          <p className={css.passo}>Para imprimir sem a janela de confirmação:</p>
          <ol className={css.passos}>
            <li>Escolha a impressora térmica como padrão do Windows.</li>
            <li>
              Abra o Chrome deste PC por um atalho com <code>--kiosk-printing</code>. No destino do atalho:
              <code className={css.atalho}>{atalhoDoChrome(enderecoDoConsole())}</code>
            </li>
            <li>Abra o sistema por esse atalho e mantenha a janela aberta.</li>
          </ol>
        </div>
      )}
    </div>
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
      <h3 id={`${id}-papeis`}>Impressora e papel neste aparelho</h3>
      <p className={css.descricao}>
        Escolha o padrão deste aparelho. Você pode trocar a impressora e o papel ao imprimir.
      </p>
      <div className={css.papeis}>
        {DOCUMENTOS.map((documento) => {
          const perfil = preferencias.perfis[documento.valor]
          const papel = preferencias.papeis[documento.valor]
          return (
            <fieldset key={documento.valor} className={css.documento}>
              <legend>{documento.rotulo}</legend>
              <CampoSelecao
                rotulo="Impressora"
                opcoes={PERFIS_IMPRESSAO}
                value={perfil}
                onChange={(evento) => aplicar(trocarPerfilPadrao(preferencias, documento.valor, evento.target.value))}
              />
              <CampoSelecao
                rotulo="Papel"
                opcoes={PAPEIS.filter((p) => perfilDeImpressao(perfil).papeis.includes(p.valor))}
                value={papel}
                onChange={(evento) => aplicar(trocarPapelPadrao(preferencias, documento.valor, evento.target.value))}
              />
              <p className={css.descricao}>{orientacaoDoPapel(papel)}</p>
            </fieldset>
          )
        })}
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
      <CanhotoSozinho ligado={preferencias.canhotoAutomatico} aoMudar={(ligado) => aplicar(trocarCanhotoAutomatico(preferencias, ligado, Date.now()))} />
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
