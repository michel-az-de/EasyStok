// Frente Anexos (rodada 7, pedido do dono 24/09/2026 04h12: "cadastro de
// fotos crud também interativo com nome e descrição... poder enviar ali na
// hora"). Tela cheia de propósito (Modal, não Popover): cadastro de foto com
// nome e descrição é formulário de verdade, não cabe em menu ancorado.
//
// "Prato vira parte da galeria" (registro em auditoria/decisoes/61-anexos.md):
// esta tela SUBSTITUI o antigo botão "Prato" do composer. Escolher e mandar
// a carta de um prato e mandar uma foto cadastrada aqui eram o mesmo gesto
// com dois donos diferentes; agora é um só, com CRUD de verdade por cima.
import { useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea, CampoTexto } from '../../componentes/Campo'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { Vazio } from '../../componentes/Vazio'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { tipoDeArquivo, validarArquivo, validarPeca, mensagemDePeca } from '../../dominio/anexos'
import { aceitaFormato, canalDaConversa, motivoDeFormato } from '../../dominio/canal'
import { permissaoDeEscrita } from '../../dominio/janela'
import { useArquivoComoDataUrl } from '../../hooks/useArquivoComoDataUrl'
import css from './anexos.module.css'

// Escolher a foto da peça: só imagem (uma peça não é PDF). Mesma técnica de
// <label>+".sr" do anexo de arquivo do composer (ComposerAnexos.jsx): campo
// focável por teclado mesmo escondido, fora do seletor da varredura de
// clique (só o <label> conta).
function CampoFoto({ foto, aoEscolher, erro }) {
  const { ler } = useArquivoComoDataUrl()
  return (
    <div>
      <label className={`${css.campoFoto} ${erro ? css.campoFotoInvalido : ''}`}>
        {foto ? <img src={foto} alt="" /> : <span className={css.campoFotoVazio}><Icone nome="imagem" /> Escolher foto</span>}
        <input
          type="file"
          className="sr"
          data-campo="foto"
          aria-invalid={Boolean(erro)}
          accept="image/jpeg,image/png,image/webp,image/gif"
          onChange={async (evento) => {
            const arquivo = evento.target.files?.[0]
            evento.target.value = ''
            if (!arquivo) return
            const { aceito } = validarArquivo({ tipo: arquivo.type, tamanho: arquivo.size })
            if (!aceito || tipoDeArquivo(arquivo.type) !== 'imagem') return
            aoEscolher(await ler(arquivo))
          }}
        />
      </label>
      {erro && <p className={css.dicaFoto}>{erro}</p>}
    </div>
  )
}

// Incluir e editar no mesmo formulário (mesmo molde de
// `features/cardapio/PainelCardapio.jsx:FormularioItemCardapio`): campos
// iguais, só muda o rótulo do botão e o que acontece ao confirmar.
function FormularioPeca({ peca, aoFechar, aoIncluir, aoEditar }) {
  const [nome, setNome] = useState(peca?.nome ?? '')
  const [descricao, setDescricao] = useState(peca?.descricao ?? '')
  const [foto, setFoto] = useState(peca?.foto ?? null)
  const [problemas, setProblemas] = useState([])
  const formularioRef = useRef(null)

  const erroDoCampo = (campo) => problemas.find((p) => p.campo === campo)?.mensagem
  // Corrigir o campo apaga a marca dele na hora: reprovar de novo é achado
  // novo, não eco do clique anterior.
  const limparProblema = (campo) => setProblemas((atual) => atual.filter((p) => p.campo !== campo))

  // Nunca clique sem resposta: campo vazio marca, mostra a frase curta e foca
  // o primeiro da lista (validarPeca já devolve na ordem visual do formulário).
  function confirmar() {
    const dados = { nome, descricao, foto }
    const encontrados = validarPeca(dados)
    setProblemas(encontrados)
    if (encontrados.length > 0) {
      formularioRef.current?.querySelector(`[data-campo="${encontrados[0].campo}"]`)?.focus()
      return
    }
    if (peca) aoEditar(peca.id, dados)
    else aoIncluir(dados)
  }

  return (
    <Modal
      titulo={peca ? `Editar ${peca.nome}` : 'Nova peça'}
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao onClick={aoFechar}>Cancelar</Botao>
          <Botao variante="primario" onClick={confirmar}>{peca ? 'Salvar' : 'Incluir na galeria'}</Botao>
        </>
      )}
    >
      <div className={css.formulario} ref={formularioRef}>
        <CampoFoto
          foto={foto}
          aoEscolher={(url) => { setFoto(url); limparProblema('foto') }}
          erro={erroDoCampo('foto')}
        />
        <CampoTexto
          rotulo="Nome"
          value={nome}
          onChange={(e) => { setNome(e.target.value); limparProblema('nome') }}
          placeholder="Ex.: Lasanha clássica, Horário de funcionamento"
          data-campo="nome"
          aria-invalid={Boolean(erroDoCampo('nome'))}
          dica={erroDoCampo('nome')}
        />
        <CampoArea
          rotulo="Descrição"
          value={descricao}
          onChange={(e) => { setDescricao(e.target.value); limparProblema('descricao') }}
          placeholder="O que o cliente precisa saber ao ver essa foto."
          data-campo="descricao"
          aria-invalid={Boolean(erroDoCampo('descricao'))}
          dica={erroDoCampo('descricao')}
        />
        {problemas.length > 0 && (
          <p className="sr" role="alert">
            Preencha antes de continuar: {problemas.map((p) => p.mensagem).join(' ')}
          </p>
        )}
      </div>
    </Modal>
  )
}

export function PainelGaleria({ aoFechar }) {
  const { selecionada, agora, fonteApi } = useAtendimento()
  const { canais, galeria } = useCatalogo()
  const {
    enviarMidia, incluirPeca, editarPeca, tirarPeca,
  } = useAcoes()
  const [modal, setModal] = useState(null) // { modo: 'novo' } | { modo: 'editar', peca }
  const [confirmandoTirar, setConfirmandoTirar] = useState(null)
  const [enviadoId, setEnviadoId] = useState(null)
  const [enviandoId, setEnviandoId] = useState(null)

  const canal = canalDaConversa(canais, selecionada)
  const { pode, motivo } = permissaoDeEscrita(selecionada, agora, canal)
  const podeEnviarFoto = pode && aceitaFormato(canal, 'foto')
  const motivoBloqueio = !pode ? motivo?.detalhe : motivoDeFormato(canal, 'foto')

  async function aoCliqueEnviar(peca) {
    setEnviandoId(peca.id)
    const enviado = await enviarMidia(selecionada.id, mensagemDePeca(peca))
    setEnviandoId(null)
    if (enviado === false) return
    setEnviadoId(peca.id)
    setTimeout(() => setEnviadoId((atual) => (atual === peca.id ? null : atual)), 1400)
  }

  function aoCliqueTirar(id) {
    if (confirmandoTirar === id) {
      tirarPeca(id)
      setConfirmandoTirar(null)
    } else {
      setConfirmandoTirar(id)
    }
  }

  return (
    <Modal
      titulo="Fotos e peças"
      descricao={fonteApi
        ? 'Fotos dos pratos do cardápio, prontas para enviar na conversa.'
        : 'Fotos prontas para enviar na conversa: prato, horário de funcionamento, ou o que você cadastrar.'}
      largura="720px"
      aoFechar={aoFechar}
      rodape={<Botao onClick={aoFechar}>Fechar</Botao>}
    >
      {motivoBloqueio && <p className={css.aviso}><Icone nome="alerta" /> {motivoBloqueio}</p>}

      {galeria.length === 0 ? (
        <Vazio
          titulo={fonteApi ? 'Nenhum prato com foto' : 'Nenhuma peça cadastrada'}
          acao={!fonteApi && <Botao variante="primario" onClick={() => setModal({ modo: 'novo' })}>Nova peça</Botao>}
        >
          {/* #1474: no modo API não há cadastro de peça aqui; prometer isso era um beco sem saída. */}
          {fonteApi
            ? 'As fotos vêm dos pratos do cardápio. Nenhum prato tem foto ainda.'
            : 'Cadastre uma foto com nome e descrição para mandar na conversa em um toque.'}
        </Vazio>
      ) : (
        <ul className={css.grade}>
          {galeria.map((peca) => (
            <li key={peca.id} className={css.cartao}>
              {/* Sem loading="lazy": dentro do <dialog> a miniatura ficava branca (#1437). */}
              <img src={peca.foto} alt={peca.nome} />
              <div className={css.cartaoTexto}>
                <strong>{peca.nome}</strong>
                <p>{peca.descricao}</p>
              </div>
              <div className={css.cartaoAcoes}>
                {peca.doCardapio && <a href={peca.foto} target="_blank" rel="noreferrer">Abrir foto</a>}
                {!fonteApi && <>
                <button
                  type="button"
                  className={css.cartaoIcone}
                  aria-label={`Editar ${peca.nome}`}
                  onClick={() => { setConfirmandoTirar(null); setModal({ modo: 'editar', peca }) }}
                >
                  <Icone nome="lapis" />
                </button>
                <button
                  type="button"
                  className={`${css.cartaoIcone} ${confirmandoTirar === peca.id ? css.confirmarTirar : ''}`}
                  aria-label={confirmandoTirar === peca.id ? `Confirmar exclusão de ${peca.nome}` : `Tirar ${peca.nome} da galeria`}
                  onClick={() => aoCliqueTirar(peca.id)}
                >
                  <Icone nome="lixeira" />
                  {confirmandoTirar === peca.id && <span>Confirmar</span>}
                </button>
                </>}
                <Botao
                  variante="primario"
                  className={css.cartaoEnviar}
                  disabled={!podeEnviarFoto || enviandoId !== null || (fonteApi && selecionada?.canal !== 'WhatsApp')}
                  title={motivoBloqueio ?? undefined}
                  onClick={() => aoCliqueEnviar(peca)}
                >
                  {enviandoId === peca.id ? 'Enviando…' : enviadoId === peca.id ? <><Icone nome="check" /> Enviado</> : 'Enviar'}
                </Botao>
              </div>
            </li>
          ))}
          {!fonteApi && <li className={css.cartaoNovo}>
            <button type="button" onClick={() => setModal({ modo: 'novo' })}>
              <Icone nome="plus" /> Nova peça
            </button>
          </li>}
        </ul>
      )}

      {modal && (
        <FormularioPeca
          peca={modal.modo === 'editar' ? modal.peca : null}
          aoFechar={() => setModal(null)}
          aoIncluir={(dados) => { incluirPeca(dados, agora); setModal(null) }}
          aoEditar={(id, dados) => { editarPeca(id, dados); setModal(null) }}
        />
      )}
    </Modal>
  )
}
