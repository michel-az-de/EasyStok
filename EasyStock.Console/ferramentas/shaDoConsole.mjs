// #2393: a meta `easystok-console-sha` liga o aviso de versão nova (AtualizacaoDoConsole). Sem ela o
// aviso nunca liga e ninguém percebe. No build de produção (modo API) o SHA é obrigatório: vem de
// CONSOLE_SHA (o console-deploy.sh exporta) ou do git; sem um dos dois, o build falha.
const SHA = /^[a-f0-9]{40}$/

export function resolverShaDoConsole({ env = {}, lerGit = () => '' } = {}) {
  const sha = (env.CONSOLE_SHA || lerGit() || '').trim()
  return SHA.test(sha) ? sha : null
}

export function plugin({ exigir, env, lerGit }) {
  return {
    name: 'easystok-console-sha',
    apply: 'build',
    transformIndexHtml(html) {
      const sha = resolverShaDoConsole({ env, lerGit })
      if (!sha) {
        if (exigir) throw new Error('Build de produção sem o SHA do console: defina CONSOLE_SHA com 40 caracteres hex (o aviso de versão nova depende da meta easystok-console-sha).')
        return html
      }
      return html.replace('<head>', `<head><meta name="easystok-console-sha" content="${sha}">`)
    },
  }
}
