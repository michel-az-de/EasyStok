// Testes — #2317 (SYNC-8) modal de conflito do PWA
//
// O servidor devolve rejected[] com `mutationId` (sync.js). O modal e o log de
// conflito liam `c.id`, sempre vazio, e mostravam o sufixo de '' no lugar do
// identificador real da mutation.
'use strict';

module.exports = function ({ test, sandbox, assert }) {
  test('SYNC-8: modal de conflito mostra o sufixo do mutationId, não de c.id', () => {
    const doc = sandbox.document;
    const origGet = doc.getElementById;
    const overlay = {
      innerHTML: '', style: {}, className: '', dataset: {},
      classList: { add() {}, remove() {}, toggle() {}, contains() { return false; } },
      setAttribute() {}, appendChild() {}, addEventListener() {},
      querySelector() { return null; }, querySelectorAll() { return []; }
    };
    const corpo = { innerHTML: '' };
    doc.getElementById = (id) => {
      if (id === 'conflictModal') return overlay;
      if (id === 'conflictBody') return corpo;
      return origGet ? origGet.call(doc, id) : null;
    };
    try {
      sandbox.showConflictModal([{ mutationId: 'mut_ab12cd34ef56', reason: 'conflict: pedido editado por outro' }]);
    } finally {
      doc.getElementById = origGet;
    }
    assert.ok(corpo.innerHTML.includes('ef56'), 'modal deve exibir o final do mutationId');
  });
};
