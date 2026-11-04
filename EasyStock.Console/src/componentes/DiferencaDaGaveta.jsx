import { Pilula } from './Pilula'

// #2078 (E5): diferença entre o que ela contou e o esperado na gaveta, usada no Fechar o caixa e no
// fechamento do caixa esquecido (Abrir a loja). A região existe antes do valor (senão o leitor de tela
// não anuncia) e a gravidade vai em palavra e ícone, não só na cor do ponto. `conferencia` é a de
// `dominio/caixa.js: conferenciaDaGaveta` (o componente não importa o domínio).
export function DiferencaDaGaveta({ conferencia, className }) {
  return (
    <output className={className} aria-live="polite">
      {conferencia.pronta && (
        <>
          Diferença: <Pilula tom={conferencia.tom} icone={conferencia.tom === 'ok' ? 'check' : 'alerta'}>{conferencia.texto}</Pilula>
          {conferencia.gravidade && <span>{conferencia.gravidade}</span>}
        </>
      )}
    </output>
  )
}
