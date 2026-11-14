// Cliente da API EasyStok para os MCPs operacionais. Login nominal (JWT do usuario); a senha so vem do ambiente local.
const BASE = (process.env.BABA_API_BASE || 'https://api.easystok.online').replace(/\/+$/, '');
const LOCAL = /^http:\/\/(localhost|127\.0\.0\.1|\[::1\])(:\d+)?$/;
const PERMITIDO = /^\/api\/(auth\/login|caixa\/[a-z/]+)(\?[A-Za-z0-9=&_.:%-]*)?$/;

export function validarBase(base) {
  if (!/^https:\/\/[A-Za-z0-9.-]+(:\d+)?$/.test(base) && !LOCAL.test(base)) throw new Error('BABA_API_BASE deve ser https (ou http local).');
  return base;
}

/** Cria um cliente com cache do token. `env` e `fetchFn` existem para teste. */
export function criarCliente({ base = BASE, env = process.env, fetchFn = fetch } = {}) {
  validarBase(base);
  let token = null;
  const req = async (metodo, caminho, corpo, tentouLogin = false) => {
    if (!PERMITIDO.test(caminho)) throw new Error('caminho da API nao permitido.');
    if (!token && caminho !== '/api/auth/login') await entrar();
    const r = await fetchFn(base + caminho, {
      method: metodo, signal: AbortSignal.timeout(20_000),
      headers: { 'content-type': 'application/json', ...(token ? { authorization: `Bearer ${token}` } : {}) },
      body: corpo === undefined ? undefined : JSON.stringify(corpo),
    });
    if (r.status === 401 && !tentouLogin && caminho !== '/api/auth/login') { token = null; await entrar(); return req(metodo, caminho, corpo, true); }
    const txt = await r.text();
    if (!r.ok) throw new Error(`API ${r.status}: ${txt.slice(0, 300)}`);
    return txt ? JSON.parse(txt) : null;
  };
  async function entrar() {
    const email = env.BABA_API_EMAIL, senha = env.BABA_API_PASSWORD;
    if (!email || !senha) throw new Error('defina BABA_API_EMAIL e BABA_API_PASSWORD no ambiente local.');
    const corpo = { email, senha, ...(env.BABA_EMPRESA_ID ? { empresaId: env.BABA_EMPRESA_ID } : {}) };
    const r = await req('POST', '/api/auth/login', corpo);
    token = r?.data?.token;
    if (!token) throw new Error('login sem token.');
    return r.data.usuario;
  }
  return { get: c => req('GET', c), post: (c, b) => req('POST', c, b), entrar, empresaId: () => env.BABA_EMPRESA_ID || null };
}
