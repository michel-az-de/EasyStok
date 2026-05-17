// Modal de login JWT — overlay + form + chamada POST /api/auth/login.

import * as auth from '../auth.js';
import { icon } from './icons.js';

export function openLoginModal(onSuccess) {
    closeLoginModal();
    const modal = document.createElement('div');
    modal.className = 'es-modal';
    modal.id = 'es-login-modal';
    modal.innerHTML = `
        <div class="es-modal-backdrop" data-close></div>
        <div class="es-modal-card" role="dialog" aria-modal="true" aria-labelledby="es-login-title">
            <header class="es-modal-head">
                <div>
                    <h2 id="es-login-title">Autenticar</h2>
                    <p class="es-modal-sub">Faça login para obter um token JWT e testar endpoints autenticados.</p>
                </div>
                <button class="es-modal-close" type="button" aria-label="Fechar" data-close>${icon('close', 18)}</button>
            </header>
            <form class="es-modal-body" id="es-login-form" autocomplete="on" novalidate>
                <label class="es-field">
                    <span class="es-field-label">Email <em>*</em></span>
                    <input type="email" name="email" required autocomplete="username" placeholder="usuario@empresa.com.br">
                </label>
                <label class="es-field">
                    <span class="es-field-label">Senha <em>*</em></span>
                    <input type="password" name="senha" required autocomplete="current-password" minlength="1" placeholder="••••••••">
                </label>
                <label class="es-field">
                    <span class="es-field-label">EmpresaId <small>(opcional — UUID da empresa)</small></span>
                    <input type="text" name="empresaId" placeholder="3fa85f64-5717-4562-b3fc-2c963f66afa6">
                </label>
                <div class="es-modal-error" id="es-login-error" role="alert" hidden></div>
                <div class="es-modal-actions">
                    <button type="button" class="es-btn es-btn-ghost" data-close>Cancelar</button>
                    <button type="submit" class="es-btn es-btn-primary" id="es-login-submit">
                        ${icon('lock', 14)} <span>Entrar</span>
                    </button>
                </div>
            </form>
        </div>
    `;
    document.body.appendChild(modal);
    document.body.style.overflow = 'hidden';

    const form = modal.querySelector('#es-login-form');
    const errEl = modal.querySelector('#es-login-error');
    const submitBtn = modal.querySelector('#es-login-submit');
    const submitLabel = submitBtn.querySelector('span');

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        errEl.hidden = true;
        errEl.textContent = '';
        submitBtn.disabled = true;
        submitLabel.textContent = 'Autenticando…';
        try {
            const fd = new FormData(form);
            const email = (fd.get('email') || '').toString().trim();
            const senha = (fd.get('senha') || '').toString();
            const empresaId = (fd.get('empresaId') || '').toString().trim() || null;
            if (!email || !senha) throw new Error('Email e senha são obrigatórios');
            await auth.login(email, senha, empresaId);
            closeLoginModal();
            if (typeof onSuccess === 'function') onSuccess();
        } catch (err) {
            errEl.textContent = err.message || String(err);
            errEl.hidden = false;
            submitBtn.disabled = false;
            submitLabel.textContent = 'Entrar';
        }
    });

    modal.addEventListener('click', (e) => {
        if (e.target.closest('[data-close]')) closeLoginModal();
    });

    function escHandler(e) {
        if (e.key === 'Escape') {
            closeLoginModal();
            window.removeEventListener('keydown', escHandler);
        }
    }
    window.addEventListener('keydown', escHandler);

    setTimeout(() => modal.querySelector('input[name="email"]')?.focus(), 60);
}

export function closeLoginModal() {
    const m = document.getElementById('es-login-modal');
    if (m) m.remove();
    document.body.style.overflow = '';
}
