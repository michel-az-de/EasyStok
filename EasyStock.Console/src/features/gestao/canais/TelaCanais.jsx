// Configurações › Canais (#1447, homologação de 07/10): "Conectar o WhatsApp
// Business" saiu do meio da Gestão e virou tela própria. Mostra o estado real
// do canal (`GET api/integracoes/whatsapp/status`), o botão de conectar por
// coexistência (#1417) e o caminho para as telas onde o canal aparece.
import { Icone } from '../../../componentes/Icone'
import { Pilula } from '../../../componentes/Pilula'
import { Botao } from '../../../componentes/Botao'
import { useAtendimento } from '../../../aplicacao/contextos'
import { useStatusWhatsAppApi } from '../../../aplicacao/useStatusWhatsAppApi'
import { resumoDoWhatsApp } from '../../../dominio/canais'
import { hashDoModulo } from '../../../dominio/modulos'
import { SecaoWhatsApp } from '../integracoes/SecaoWhatsApp'
import css from './TelaCanais.module.css'

const LIGADAS = [
  { href: hashDoModulo('atendimento'), icone: 'conversa', titulo: 'Balcão', detalhe: 'As conversas do WhatsApp chegam aqui' },
  {
    href: hashDoModulo('atendimento', 'horarios'), icone: 'relogio',
    titulo: 'Horários e mensagens', detalhe: 'O que o cliente recebe com a loja fechada',
  },
  { href: hashDoModulo('financeiro'), icone: 'dollar-sign', titulo: 'Caixa do dia', detalhe: 'Pagamentos dos pedidos fechados na conversa' },
]

function EstadoDoCanal() {
  const { agora } = useAtendimento()
  const { carregando, status, erro, recarregar } = useStatusWhatsAppApi()
  const resumo = status === undefined ? null : resumoDoWhatsApp(status, agora)
  return (
    <>
      <div className={css.estado} aria-live="polite">
        {carregando && !resumo && <p className={css.apoio}>Conferindo o canal…</p>}
        {resumo && (
          <>
            <Pilula tom={resumo.tom}>{resumo.titulo}</Pilula>
            <ul className={css.linhas}>
              {resumo.linhas.map((linha) => <li key={linha}>{linha}</li>)}
            </ul>
          </>
        )}
        {erro && (
          <p className={css.erro} role="alert">
            O estado do canal não carregou: {erro}{' '}
            <Botao variante="texto" onClick={recarregar}>Tentar de novo</Botao>
          </p>
        )}
      </div>
      <SecaoWhatsApp embutida aoConectar={recarregar} />
    </>
  )
}

export function TelaCanais() {
  return (
    <div className={css.tela}>
      <section className={css.canal} aria-labelledby="canal-whatsapp">
        <header className={css.cabeca}>
          <span className={css.selo}><Icone nome="whatsapp" tamanho={28} /></span>
          <div>
            <h2 id="canal-whatsapp" className={css.titulo}>WhatsApp Business</h2>
            <p className={css.apoio}>O número da loja continua no app do celular e responde também pelo EasyStok.</p>
          </div>
        </header>
        <EstadoDoCanal />
      </section>

      <nav className={css.ligadas} aria-labelledby="canal-ligadas">
        <h3 id="canal-ligadas" className={css.subtitulo}>Onde o canal aparece</h3>
        <ul className={css.listaLigadas}>
          {LIGADAS.map((item) => (
            <li key={item.href}>
              <a className={css.ligada} href={item.href}>
                <Icone nome={item.icone} tamanho={22} />
                <span className={css.textoLigada}>
                  <strong>{item.titulo}</strong>
                  <span>{item.detalhe}</span>
                </span>
                <Icone nome="chevron-right" tamanho={18} />
              </a>
            </li>
          ))}
        </ul>
      </nav>

      <p className={css.apoio}>Instagram, Messenger e e-mail entram nesta tela quando forem ligados.</p>
    </div>
  )
}
