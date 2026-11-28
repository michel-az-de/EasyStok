// Prova da #3401: o hook PostToolUse barra, logo depois da edição, o erro típico de código escrito por IA.
//
// 0. o .claude/settings.json liga este hook no PostToolUse de Edit|Write|MultiEdit.
// 1. import de arquivo que não existe (`import x from './naoExiste'`), regra local/import-existe; também com
//    caixa errada no arquivo ou na pasta, com o caminho do payload em barra normal e com payload de 600 KB
//    (o fast-gate findstr calava acima de 128 KiB por linha; revisão da #3404).
// 2. import de pacote fora do package.json, mesma regra.
// 3. import de export que não existe (import/named) e membro inventado de `import * as` (import/namespace).
// 4. variável sem uso (no-unused-vars).
// 5. hook do React chamado dentro de if (react/rules-of-hooks).
// 6. arquivo limpo (relativo, CSS module, pacote, import dinâmico, query ?raw) passa sem ruído.
// 7. arquivo fora de src passa: o hook só olha EasyStock.Console/src.
// 8. muitos erros saem em até 10 linhas.
// Um caso por arquivo, cada um conferindo o id da regra que barrou.
//
// O Console é montado num diretório temporário com a config e a regra local reais (cópia), para não
// escrever em src enquanto outras provas rodam em paralelo. O oxlint vem do node_modules deste checkout.
//
// Roda com: node ferramentas/prova-3401-lint-na-edicao.mjs

import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const CONSOLE = fileURLToPath(new URL('..', import.meta.url))
const HOOK = fileURLToPath(new URL('../../scripts/poka-yoke/posttooluse-lint-console.mjs', import.meta.url))
const SETTINGS = fileURLToPath(new URL('../../.claude/settings.json', import.meta.url))
const TETO_DE_LINHAS = 10

const ligados = JSON.parse(readFileSync(SETTINGS, 'utf8')).hooks.PostToolUse
  .filter((grupo) => ['Edit', 'Write', 'MultiEdit'].every((ferramenta) => grupo.matcher.split('|').includes(ferramenta)))
  .flatMap((grupo) => grupo.hooks)
  .filter((gancho) => gancho.command === 'node' && gancho.args?.some((arg) => arg.endsWith('posttooluse-lint-console.mjs')))
assert.equal(ligados.length, 1, 'o .claude/settings.json precisa ligar o posttooluse-lint-console.mjs uma vez no PostToolUse de Edit|Write|MultiEdit')
console.log('ok - settings.json liga o hook em Edit|Write|MultiEdit')

const tmp = mkdtempSync(join(tmpdir(), 'prova-3401-'))
const raiz = join(tmp, 'EasyStock.Console')
mkdirSync(join(raiz, 'src'), { recursive: true })
mkdirSync(join(raiz, 'ferramentas'), { recursive: true })
for (const arquivo of ['.oxlintrc.json', 'package.json', join('ferramentas', 'oxlint-local.mjs')]) {
  copyFileSync(join(CONSOLE, arquivo), join(raiz, arquivo))
}
writeFileSync(join(raiz, 'src', 'util.js'), 'export const existe = 1\nexport default function padrao() { return 2 }\n')
writeFileSync(join(raiz, 'src', 'outro.js'), 'export const outro = 3\n')
writeFileSync(join(raiz, 'src', 'estilo.module.css'), '.a { color: red }\n')
mkdirSync(join(raiz, 'src', 'pasta'))
writeFileSync(join(raiz, 'src', 'pasta', 'fundo.js'), 'export const fundo = 4\n')

// O hook responde no padrão do repo (pretooluse-block-generated.ps1): JSON no stdout e exit 0.
// `decision: block` faz o Claude Code mostrar o `reason` ao agente logo depois da edição.
function editar(relativo, codigo, { ferramenta = 'Write', barra = false, lastro = '' } = {}) {
  const caminho = join(raiz, relativo)
  writeFileSync(caminho, codigo)
  const alvo = barra ? caminho.replaceAll('\\', '/') : caminho
  const payload = { hook_event_name: 'PostToolUse', tool_name: ferramenta, tool_input: { file_path: alvo, content: lastro }, tool_response: { filePath: alvo, content: lastro } }
  const inicio = Date.now()
  const r = spawnSync(process.execPath, [HOOK], { input: JSON.stringify(payload), encoding: 'utf8', env: { ...process.env, CLAUDE_PROJECT_DIR: '' } })
  let resposta = null
  try { resposta = r.stdout.trim() ? JSON.parse(r.stdout) : null } catch { /* a asserção abaixo mostra o stdout */ }
  return { codigo: r.status, saida: r.stdout, resposta, motivo: resposta?.reason ?? '', ms: Date.now() - inicio }
}

function barrado(nome, r, ...trechos) {
  assert.equal(r.codigo, 0, `${nome}: o hook sai com 0 e responde em JSON, veio ${r.codigo}\n${r.saida}`)
  assert.equal(r.resposta?.decision, 'block', `${nome}: esperava decision block, veio\n${r.saida}`)
  for (const trecho of trechos) assert.ok(r.motivo.includes(trecho), `${nome}: faltou "${trecho}" em\n${r.motivo}`)
  console.log(`ok - ${nome} barrado em ${r.ms} ms`)
}

function passa(nome, r) {
  assert.equal(r.codigo, 0, `${nome}: esperava exit 0, veio ${r.codigo}\n${r.saida}`)
  assert.equal(r.saida.trim(), '', `${nome}: não pode gerar ruído, veio\n${r.saida}`)
  console.log(`ok - ${nome} passa em ${r.ms} ms`)
}

try {
  barrado('import de arquivo inexistente', editar(join('src', 'Inventado.jsx'),
    "import x from './naoExiste'\nexport function Inventado() { return <p>{x}</p> }\n"), './naoExiste', 'import-existe')

  barrado('pacote fora do package.json', editar(join('src', 'pacote.js'),
    "import z from 'pacote-que-ninguem-instalou'\nexport const usa = z\n"), 'pacote-que-ninguem-instalou', 'import-existe')

  barrado('import com caixa errada (o Windows aceita, o CI Linux não)', editar(join('src', 'Caixa.js'),
    "import { existe } from './Util'\nexport const usa = existe\n"), './Util', 'import-existe')

  barrado('import com caixa errada na pasta', editar(join('src', 'CaixaPasta.js'),
    "import { fundo } from './Pasta/fundo'\nexport const usa = fundo\n"), './Pasta/fundo', 'import-existe')

  barrado('payload de 600 KB', editar(join('src', 'Grande.js'),
    "import x from './naoExiste'\nexport const usa = x\n", { lastro: 'y'.repeat(300_000) }), './naoExiste', 'import-existe')

  barrado('caminho com barra normal no payload', editar(join('src', 'Barra.js'),
    "import x from './naoExiste'\nexport const usa = x\n", { barra: true }), './naoExiste', 'import-existe')

  barrado('export inexistente', editar(join('src', 'exportInventado.js'),
    "import { naoTem } from './util'\nexport const usa = naoTem\n", { ferramenta: 'Edit' }), 'naoTem', 'import(named)')

  barrado('membro inexistente de namespace', editar(join('src', 'namespace.js'),
    "import * as U from './util'\nexport const usa = U.inventado\n"), 'inventado', 'import(namespace)')

  barrado('variável sem uso', editar(join('src', 'semUso.js'),
    'const sobra = 1\nexport const usada = 2\n', { ferramenta: 'MultiEdit' }), 'sobra', 'no-unused-vars')

  barrado('hook condicional', editar(join('src', 'Hook.jsx'),
    "import { useState } from 'react'\nexport function Hook({ ok }) {\n  if (ok) { const [v] = useState(0); return v }\n  return null\n}\n"), 'useState', 'rules-of-hooks')

  passa('arquivo limpo', editar(join('src', 'Limpo.jsx'), [
    "import { useState } from 'react'",
    "import padrao, { existe } from './util'",
    "import { outro } from './outro.js'",
    "import css from './estilo.module.css'",
    "import bruto from './estilo.module.css?raw'",
    "export { existe as reexporta } from './util'",
    'export function Limpo() {',
    '  const [v] = useState(existe + outro + padrao())',
    "  const carregar = () => import('./util')",
    '  return <p className={css.a} onClick={carregar}>{v}{bruto}</p>',
    '}',
    '',
  ].join('\n')))

  passa('arquivo fora de src', editar(join('ferramentas', 'fora.js'), "import x from './naoExiste'\nexport const y = x\n"))

  const muitos = editar(join('src', 'muitos.js'), Array.from({ length: 15 }, (_, i) => `const sobra${i} = ${i}`).join('\n') + '\n')
  barrado('muitos erros', muitos, 'sobra0', 'mais 5')
  const linhas = muitos.motivo.split('\n').filter((linha) => /:\d+:\d+: /.test(linha))
  assert.ok(linhas.length <= TETO_DE_LINHAS, `devolveu ${linhas.length} linhas de erro, teto ${TETO_DE_LINHAS}`)
  console.log(`ok - 15 erros saem em ${linhas.length} linhas`)
} finally {
  rmSync(tmp, { recursive: true, force: true })
}
