/*
posttooluse-lint-console -- hook PostToolUse do Claude Code (#3401, ADR-0029, aviso contextual).

Depois de Edit/Write/MultiEdit em EasyStock.Console/src/**.js|jsx, roda o oxlint SO no arquivo tocado e SO
com erros (--quiet): import de arquivo, pacote ou export que nao existe, variavel sem uso, hook do React
fora de ordem. Os avisos antigos ficam de fora. Achou erro: responde {"decision":"block","reason":...} com
ate 10 linhas, que o Claude Code mostra ao agente na hora (a edicao ja aconteceu; ele corrige no passo
seguinte). Limpo ou fora do escopo: calado. oxlint ausente ou quebrado: aviso no contexto. Sempre exit 0,
no padrao do pretooluse-block-generated.ps1 (JSON, sem misturar com exit code).

Recebe o payload via STDIN e e chamado direto pelo node, sem fast-gate cmd+findstr: o findstr cala com
linha acima de 128 KiB (Write de arquivo grande) e o %RANDOM% do arquivo temporario colide entre hooks em
paralelo, entregando o erro de um arquivo a outra edicao (medido na revisao da #3404). O node direto custa
~30 ms a mais por edicao fora do Console. Worktree sem node_modules usa o oxlint de outro checkout com a
config e o cwd dela.
Configurado em .claude/settings.json. Ver .poka-yoke/registry.yaml (traps.ia-import-inexistente).
*/
import { spawnSync } from 'node:child_process'
import { existsSync, readFileSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const TETO_DE_LINHAS = 10
const ESCOPO = /^(.*[\\/]EasyStock\.Console)[\\/]src[\\/].+\.(?:js|jsx|mjs)$/i
const REPO_DO_SCRIPT = fileURLToPath(new URL('../..', import.meta.url))

const responder = (resposta) => JSON.stringify(resposta)
const avisar = (texto) => responder({ hookSpecificOutput: { hookEventName: 'PostToolUse', additionalContext: `[poka-yoke] ${texto}` } })

// O checkout principal guarda o node_modules quando o worktree nao rodou npm ci.
function checkoutPrincipal(pasta) {
  const r = spawnSync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], { cwd: pasta, encoding: 'utf8' })
  return r.status === 0 && r.stdout.trim() ? dirname(resolve(r.stdout.trim())) : null
}

function oxlintDisponivel(raiz) {
  const candidatos = [raiz, process.env.CLAUDE_PROJECT_DIR, REPO_DO_SCRIPT]
  const binario = (pasta) => join(pasta.endsWith('EasyStock.Console') ? pasta : join(pasta, 'EasyStock.Console'), 'node_modules', 'oxlint', 'bin', 'oxlint')
  const achado = candidatos.filter(Boolean).map(binario).find((caminho) => existsSync(caminho))
  if (achado) return achado
  const principal = checkoutPrincipal(raiz)
  return principal && existsSync(binario(principal)) ? binario(principal) : null
}

function lintar(entrada) {
  if (!/EasyStock\.Console/i.test(entrada)) return null
  let arquivo
  try {
    arquivo = String(JSON.parse(entrada)?.tool_input?.file_path ?? '')
  } catch {
    return null
  }
  const casou = ESCOPO.exec(arquivo)
  if (!casou || !existsSync(arquivo)) return null

  const raiz = casou[1]
  const binario = oxlintDisponivel(raiz)
  if (!binario) return avisar(`lint pos-edicao nao rodou: oxlint ausente (rode npm ci em ${raiz}).`)

  const alvo = relative(raiz, arquivo)
  const r = spawnSync(process.execPath, [binario, '--quiet', '-f', 'unix', '-c', join(raiz, '.oxlintrc.json'), alvo], { cwd: raiz, encoding: 'utf8', timeout: 25_000 })
  const erros = `${r.stdout ?? ''}`.split(/\r?\n/).filter((linha) => /:\d+:\d+: /.test(linha))
  if (erros.length) {
    const resto = erros.length - TETO_DE_LINHAS
    return responder({
      decision: 'block',
      reason: [
        `oxlint barrou ${alvo} (${erros.length} erro${erros.length > 1 ? 's' : ''}, #3401):`,
        ...erros.slice(0, TETO_DE_LINHAS),
        ...(resto > 0 ? [`(mais ${resto}; rode npm run lint no Console)`] : []),
        'Se o import ou a variavel ainda vai ser usado ou criado no proximo passo, siga; o gate e o npm run lint.',
      ].join('\n'),
    })
  }
  if (r.error || r.status !== 0) {
    const motivo = (r.error?.message ?? `${r.stderr}${r.stdout}`).trim().split(/\r?\n/).slice(0, 3).join(' | ')
    return avisar(`lint pos-edicao nao rodou em ${alvo}: ${motivo}`)
  }
  return null
}

const saida = lintar(readFileSync(0, 'utf8'))
if (saida) process.stdout.write(saida)
