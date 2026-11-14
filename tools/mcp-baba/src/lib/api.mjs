// Cliente da API EasyStok para os MCPs operacionais. Login nominal (JWT do usuario); a senha so vem do ambiente local.
const BASE = (process.env.BABA_API_BASE || 'https://api.easystok.online').replace(/\/+$/, '');
const LOCAL = /^http:\/\/(localhost|127\.0\.0\.1|\[::1\])(:\d+)?$/;
const QUERY = '(\\?[A-Za-z0-9=&_.:%-]*)?$';
const SEG = '[a-z0-9-]+';
// Por metodo: leitura em varias areas, escrita so nas rotas que um MCP expoe (nunca por prefixo).
const PERMITIDO = {
  GET: new RegExp(`^/api/(caixa/[a-z/]+|atendimento/(producao/${SEG}(/${SEG})*|conversas(/${SEG})?(/(mensagens|assistente))?|assistente/(historico|uso)|expediente|viagens(/${SEG})?|agenda))` + QUERY),
  POST: /^\/api\/(auth\/login|caixa\/(abrir|fechar|movimentos)|atendimento\/producao\/perdas)$/,
};

export function validarBase(base) {
  if (!/^https:\/\/[A-Za-z0-9.-]+(:\d+)?$/.test(base) && !LOCAL.test(base)) throw new Error('BABA_API_BASE deve ser https (ou http local).');
  return base;
}

/** Cria um cliente com cache do token. `env` e `fetchFn` existem para teste. */
export function criarCliente({ base = BASE, env = process.env, fetchFn = fetch } = {}) {
  validarBase(base);
  let token = null, entrando = null;
  const req = async (metodo, caminho, corpo, tentouLogin = false) => {
    if (!PERMITIDO[metodo]?.test(caminho)) throw new Error('caminho da API nao permitido.');
    if (!token && caminho !== '/api/auth/login') await entrar();
    let r;
    try { r = await fetchFn(base + caminho, {
      method: metodo, signal: AbortSignal.timeout(20_000),
      headers: { 'content-type': 'application/json', ...(token ? { authorization: `Bearer ${token}` } : {}) },
      body: corpo === undefined ? undefined : JSON.stringify(corpo),
    }); } catch (e) {
      if (metodo === 'POST' && caminho !== '/api/auth/login') throw new Error(`sem resposta da API (${e.name}): a escrita PODE ter sido gravada; confira antes de repetir.`);
      throw e;
    }
    if (r.status === 401 && !tentouLogin && caminho !== '/api/auth/login') { token = null; await entrar(); return req(metodo, caminho, corpo, true); }
    const txt = await r.text();
    if (!r.ok) throw new Error(`API ${r.status}: ${txt.slice(0, 300)}`);
    return txt ? JSON.parse(txt) : null;
  };
  const entrar = () => (entrando ??= entrarUmaVez().finally(() => { entrando = null; }));
  async function entrarUmaVez() {
    const email = env.BABA_API_EMAIL, senha = env.BABA_API_PASSWORD;
    if (!email || !senha) throw new Error('defina BABA_API_EMAIL e BABA_API_PASSWORD no ambiente local.');
    const corpo = { email, senha, ...(env.BABA_EMPRESA_ID ? { empresaId: env.BABA_EMPRESA_ID } : {}) };
    const r = await req('POST', '/api/auth/login', corpo);
    token = r?.data?.token;
    if (!token) throw new Error('login sem token.');
    return r.data.usuario;
  }
  return { get: c => req('GET', c), post: (c, b) => req('POST', c, b), entrar };
}
