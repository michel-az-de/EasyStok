// Moldura do módulo (#1447): barra fina com a volta ao hall, o nome do módulo e
// o menu próprio dele (as telas do catálogo). O módulo e a tela vêm da rota
// (ADR-0046 D1); nada aqui guarda estado. Não lê contexto do Atendimento porque
// também envolve a fila da Cozinha e o painel de Entregas, que montam fora dele.
//
// `operacao`: tela de trabalho (Cozinha, Entregas) ocupa a altura toda; tela de
// ajuste ganha título e coluna de leitura. Módulo sem tela mostra "Em breve".
import { useState } from 'react'
import { MarcaIcone } from '../../componentes/MarcaIcone'
import { Botao } from '../../componentes/Botao'
import { InterruptorTema } from '../../componentes/InterruptorTema'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { Vazio } from '../../componentes/Vazio'
import { Modal } from '../../componentes/Modal'
import { HASH_HALL } from '../../dominio/rota'
import { TELA_OPERACAO, hashDoModulo, moduloPorId, telasDoMenu } from '../../dominio/modulos'
import { ehJanelaAvulsa, origemForaDoBalcao } from '../../dominio/janelaAvulsa'
import { IconeDoModulo } from './IconeDoModulo'
import { MenuDosModulos } from './MenuDosModulos'
import css from './moldura.module.css'

const MODULO_DO_BALCAO = 'atendimento'
const HASH_BALCAO = hashDoModulo(MODULO_DO_BALCAO, 'balcao')

// A janela que abriu esta (o Balcão), se ainda estiver aberta.
function janelaDeOrigem() {
  try {
    const origem = window.opener
    return origem && !origem.closed ? origem : null
  } catch { return null }
}

// #1616: na janela avulsa a volta é para o Balcão que já está aberto, não um segundo Balcão
// aqui dentro. Se a origem saiu do Balcão, volta para ele; se fechou, esta janela assume.
function voltarAoBalcao(fonteApi) {
  const origem = janelaDeOrigem()
  if (!origem) { window.location.hash = HASH_BALCAO; return }
  try {
    if (origemForaDoBalcao(origem.location.hash, { fonteApi })) origem.location.hash = HASH_BALCAO
  } catch { /* origem em outro endereço: só vem para a frente */ }
  origem.focus()
  // Aba de tablet e navegador que não troca de janela deixam o foco aqui: fechar esta janela
  // devolve a pessoa ao Balcão.
  setTimeout(() => { if (document.hasFocus()) window.close() }, 250)
}

export function MolduraDoModulo({ moduloId, telaId, fonteApi, operacao = false, tituloVisivel = true, ajuda, children }) {
  const { permite, aoSair, sessaoPersistente, notificacoes } = useAcessoModulos()
  const modulo = moduloPorId(moduloId)
  const telas = telasDoMenu(modulo, { fonteApi })
  const tela = telas.find((t) => t.id === telaId) ?? null
  const [menuAberto, setMenuAberto] = useState(false)
  // #1870: módulo de trabalho (Entregas, Cozinha, Atendimento) tem poucas telas e abas próprias;
  // o menu lateral de 216 px deixava ~650 px vazios. As telas vão para a barra, ao lado do nome.
  const telasNaBarra = telas.length > 1 && telas.some((t) => t.tipo === TELA_OPERACAO)
  const avulsa = ehJanelaAvulsa({ nome: window.name, origemAberta: janelaDeOrigem() !== null })

  return (
    <div className={css.moldura} data-conteudo-console>
      <aside className={css.lateral}>
        <MenuDosModulos moduloId={moduloId} telaId={telaId} fonteApi={fonteApi} semMenu={telasNaBarra} />
      </aside>
      <div className={css.areaTrabalho}>
      <header className={css.barra}>
        <div className={css.contexto}>
          <Botao className={css.abrirMenu} icone="painel" aria-label={`Menu de ${modulo.nome}`} aria-haspopup="dialog" aria-expanded={menuAberto} onClick={() => setMenuAberto(true)}><span>Menu<span className={css.nomeNoMenu}>{modulo.nome}</span></span></Botao>
          {avulsa ? (
            <button type="button" className={`${css.voltar} ${css.voltarBotao}`} onClick={() => voltarAoBalcao(fonteApi)}>
              <MarcaIcone nome="anterior" tamanho={20} /> <span>Voltar ao Balcão</span>
            </button>
          ) : (
            <>
              <a className={css.voltar} href={HASH_HALL}>
                <MarcaIcone nome="anterior" tamanho={20} /> <span>Módulos</span>
              </a>
              {/* #1474: quem veio da barra lateral do Balcão volta para ele num toque. */}
              {modulo.id !== MODULO_DO_BALCAO && permite(MODULO_DO_BALCAO) && (
                <a className={css.voltar} href={HASH_BALCAO}>
                  <MarcaIcone nome="mensagem" tamanho={24} /> <span>Balcão</span>
                </a>
              )}
            </>
          )}
          <span className={css.modulo}>
            <IconeDoModulo modulo={modulo} /> {modulo.nome}
          </span>
          {telasNaBarra && (
            <nav className={css.telasNaBarra} aria-label={`Telas de ${modulo.nome}`}>
              {telas.map((item) => <a key={item.id} href={hashDoModulo(modulo.id, item.id)} className={`${css.telaNaBarra} ${item.id === telaId ? css.telaAtiva : ''}`} aria-current={item.id === telaId ? 'page' : undefined}>{item.rotulo}</a>)}
            </nav>
          )}
        </div>
        <div className={css.acoes}>
          {ajuda}
          <InterruptorTema />
          {notificacoes && <div className={css.avisos}>{notificacoes}</div>}
          {aoSair && <Botao className={css.sair} variante="texto" icone="log-out" onClick={aoSair} title={sessaoPersistente ? 'A Cozinha permanece conectada ao fechar o navegador. Saia ao trocar de pessoa.' : undefined}>{sessaoPersistente ? 'Sair deste aparelho' : 'Sair'}</Botao>}
        </div>
      </header>
      {operacao
        ? <main className={css.operacao} tabIndex={-1} data-inicio-tela id="conteudo-principal" aria-label={tela?.rotulo ?? modulo.nome}>{children}</main>
        : (
          <main className={css.ajuste} tabIndex={-1} data-inicio-tela id="conteudo-principal" aria-label={tela?.rotulo ?? modulo.nome}>
            <div className={css.folha}>
              {tela ? (
                <>
                  <h1 className={tituloVisivel ? css.titulo : 'sr'}>
                    <IconeDoModulo modulo={modulo} telaId={tela.id} />
                    {tela.rotulo}
                  </h1>
                  <div className={css.conteudo}>{children}</div>
                </>
              ) : (
                <div className={css.conteudo}>
                  <Vazio titulo={`${modulo.nome}: em breve`} acao={<a className={css.link} href={HASH_HALL}>Voltar aos módulos</a>}>
                    Esta área ainda não está disponível.
                  </Vazio>
                </div>
              )}
            </div>
          </main>
        )}
      </div>
      {menuAberto && <Modal titulo={`Menu de ${modulo.nome}`} aoFechar={() => setMenuAberto(false)} largura="min(600px, calc(100vw - 24px))">
        <div className={css.menuModal}><MenuDosModulos moduloId={moduloId} telaId={telaId} fonteApi={fonteApi} semMenu={telasNaBarra} aoNavegar={(evento) => { if (evento.target.closest('a')) setMenuAberto(false) }} /></div>
      </Modal>}
    </div>
  )
}
