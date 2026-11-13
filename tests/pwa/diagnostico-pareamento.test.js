'use strict';

module.exports = function ({ test, runInSandbox, assert }) {
  for (const [code, reason] of [
    ['123456', 'Este aparelho já tem dados vinculados a outra empresa.'],
    ['12', 'Informe o código de 6 dígitos fornecido pela equipe.'],
  ]) {
    test('#2348: Parear do Diagnóstico mostra a falha no piloto: ' + code, async () => {
      const result = JSON.parse(await runInSandbox(`(async function () {
        const old = { config: window.CDB_CONFIG, prompt: window.prompt, sync: window.cdbSync,
          get: document.getElementById, toast: showToast };
        const body = { innerHTML: '' }, messages = [];
        window.CDB_CONFIG = { ...(old.config || {}), isPilot: true };
        window.prompt = () => ${JSON.stringify(code)};
        window.cdbSync = { ...old.sync, getPairing: () => null,
          pairWithCode: async () => { throw new Error(${JSON.stringify(reason)}); } };
        document.getElementById = id => id === 'diagnosticsBody' ? body : old.get.call(document, id);
        showToast = message => messages.push(message);
        try {
          openDiagnostics();
          const action = body.innerHTML.match(/onclick="([^"]+)"[^>]*>Parear com código/)[1];
          let rejected = false;
          try { await eval(action); } catch (_) { rejected = true; }
          return JSON.stringify({ rejected, messages });
        } finally {
          window.CDB_CONFIG = old.config; window.prompt = old.prompt; window.cdbSync = old.sync;
          document.getElementById = old.get; showToast = old.toast;
        }
      })()`));
      assert.equal(result.rejected, false, 'o clique deve consumir a rejeição da promise');
      assert.deepStrictEqual(result.messages, ['Falha: ' + reason]);
    });
  }
};
