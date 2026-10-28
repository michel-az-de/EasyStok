// Guard de reais (#2080): formatar e ler dinheiro é coisa de UM módulo, `src/dominio/formato.js`
// (`moeda`, `lerMoeda`, `lerReais`, `textoDosCentavos`) com o campo `CampoReais` na frente. A banca
// da #2073 achou o mesmo valor lido de quatro jeitos ("100" era R$ 1,00 no prato e R$ 100,00 na
// porção; "1.234,56" era NaN na corrida), três formatadores e dois sinais de negativo. Este guard
// reprova a volta de cada um deles, como o `RegraUnicaNoBackendTests` faz no backend (#2018).
//
// Regras:
//   R1  formatador de moeda próprio (`style: 'currency'`, `currency: 'BRL'`)
//   R2  texto de reais montado à mão (`toFixed(2).replace('.', ',')`)
//   R3  leitor de reais próprio (`.replace(',', '.')` sobre texto digitado)
//   R4  campo de reais em máscara de centavos (`<CampoMascarado tipo="moeda">`)
//   R5  campo de reais que não é `CampoReais` (`<CampoTexto rotulo="… R$ …">`, Preço, Valor, Custo)
//
// Exceção é por arquivo e regra, com o motivo. As "TEMPORÁRIAS" são pontos de outros pacotes da
// banca, ainda abertos: quando o pacote mergear a linha deixa de casar e a exceção vira inócua (o
// guard avisa, sem falhar, para ela ser apagada).
//
// Roda com: node ferramentas/verificar-reais.mjs [pasta-do-src]

import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, resolve, sep } from 'node:path'

const RAIZ = resolve(process.argv[2] ?? 'src')

const REGRAS = {
  R1: { descricao: 'formatador de moeda fora de dominio/formato.js (use `moeda`)', acha: /style:\s*['"]currency['"]|currency:\s*['"]BRL['"]/ },
  R2: { descricao: 'texto de reais montado à mão (use `textoDosCentavos`)', acha: /toFixed\(\s*2\s*\)\s*\.replace\(\s*['"]\.['"]\s*,\s*['"],['"]\s*\)/ },
  R3: { descricao: 'leitor de reais próprio (use `lerMoeda`/`lerReais`)', acha: /\.replace\(\s*['"],['"]\s*,\s*['"]\.['"]\s*\)/ },
  R4: { descricao: 'campo de reais em máscara de centavos (use `CampoReais`)', acha: /<CampoMascarado[^>]*tipo=["']moeda["']/ },
  R5: { descricao: 'campo de reais que não é `CampoReais`', acha: /<CampoTexto[^>]*rotulo=["'](?![^"']*pontos)[^"']*(R\$|Preço|Valor|Custo)/ },
}

const MODULO_UNICO = 'dominio/formato.js'
// O espelho do texto sem "R$", que `componentes` precisa porque não importa `dominio`.
const ESPELHO_DO_CAMPO = 'componentes/moedaNoCampo.js'

const PERMANENTES = [
  { arquivo: 'componentes/baixarCsv.js', regras: ['R2'], motivo: 'CSV: duas casas e vírgula, sem milhar nem R$ (formato de arquivo, não de tela)' },
  { arquivo: 'aplicacao/calculosReceita.js', regras: ['R3'], motivo: 'quantidade de insumo, não reais' },
  { arquivo: 'aplicacao/perdas.js', regras: ['R3'], motivo: 'quantidade perdida, não reais' },
  { arquivo: 'aplicacao/planejamento.js', regras: ['R3'], motivo: 'número de porções, não reais' },
  { arquivo: 'features/gestao/producao/AbaProducao.jsx', regras: ['R3'], motivo: 'peso em gramas, não reais' },
  { arquivo: 'dominio/porcoes.js', regras: ['R3'], motivo: 'peso da porção em gramas, não reais' },
  { arquivo: 'dominio/entregasApi.js', regras: ['R3'], motivo: 'distância (km) e fator de rota, não reais' },
  { arquivo: 'componentes/ModalFrete.jsx', regras: ['R5'], motivo: 'custo combinado e limite do frete externo (demonstração Lalamove, congelada): `type=number` nativo, que o navegador valida' },
]

const TEMPORARIAS = [
  { arquivo: 'features/hall/PulsoDoHall.jsx', regras: ['R1'], motivo: 'formatador `dinheiro` do Hall', issue: '#2078' },
  { arquivo: 'aplicacao/insumos.js', regras: ['R3'], motivo: 'custo do insumo ("R$ 18,90" dá "Custo inválido")', issue: '#2081' },
  { arquivo: 'features/gestao/producao/AbaInsumosApi.jsx', regras: ['R5'], motivo: 'custo por unidade do insumo', issue: '#2081' },
  { arquivo: 'features/gestao/fidelidade/AbaFidelidade.jsx', regras: ['R5'], motivo: '"Pedido mínimo (R$)" e "A cada R$" são `type=number` nativo; passar para `CampoReais` é seguimento da #2080, fora do escopo dela', issue: '#2080 (seguimento)' },
]

function arquivos(dir) {
  return readdirSync(dir).flatMap((nome) => {
    const caminho = join(dir, nome)
    if (statSync(caminho).isDirectory()) return arquivos(caminho)
    return /\.(jsx?|mjs)$/.test(caminho) ? [caminho] : []
  })
}

const excecoes = [...PERMANENTES, ...TEMPORARIAS]
const usadas = new Set()
const faltas = []

for (const caminho of arquivos(RAIZ)) {
  const arquivo = relative(RAIZ, caminho).split(sep).join('/')
  if (arquivo === MODULO_UNICO || arquivo === ESPELHO_DO_CAMPO) continue
  readFileSync(caminho, 'utf8').split('\n').forEach((linha, indice) => {
    if (/^\s*(\/\/|\*|\/\*)/.test(linha)) return
    for (const [nome, regra] of Object.entries(REGRAS)) {
      if (!regra.acha.test(linha)) continue
      const excecao = excecoes.find((e) => e.arquivo === arquivo && e.regras.includes(nome))
      if (excecao) { usadas.add(excecao); continue }
      faltas.push(`src/${arquivo}:${indice + 1}: ${nome} ${regra.descricao}`)
    }
  })
}

for (const e of TEMPORARIAS.filter((t) => !usadas.has(t))) {
  console.log(`aviso: a exceção temporária de ${e.arquivo} (${e.issue}) não casa mais; apague-a de verificar-reais.mjs`)
}

if (faltas.length > 0) {
  console.error('Reais lidos ou formatados fora do módulo único (src/dominio/formato.js):')
  for (const f of faltas) console.error('  ' + f)
  process.exit(1)
}
console.log(`verificar-reais: ok (${arquivos(RAIZ).length} arquivos, ${usadas.size} exceções em uso)`)
