/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './Pages/**/*.cshtml',
    './Pages/**/*.cs',
    './wwwroot/js/**/*.js',
  ],
  // Admin é dark-first via <html data-theme="dark">.
  darkMode: ['class', '[data-theme="dark"]'],
  safelist: [
    { pattern: /^(bg|text|border)-(navy|orange|es-)-(50|100|200|400|500|600|700|800|900)$/ },
  ],
  theme: {
    extend: {
      colors: {
        // Tokens DS
        navy: {
          900: '#06143A', 800: '#0A1F52', 700: '#0E2A6E',
          600: '#15388A', 500: '#1E48A8',
          200: '#BCC8E5', 100: '#DDE4F2', 50: '#EDF1F9',
        },
        orange: {
          700: '#BF4307', 600: '#E85814', 500: '#F26B25', 400: '#F8884A',
          200: '#FAC8AB', 100: '#FCDCC8', 50: '#FEEFE3',
        },
        // Aliases legacy via CSS vars do tokens.css (acompanham tema light/dark)
        'es-bg':        'var(--bg-app)',
        'es-surface':   'var(--bg-surface)',
        'es-surface-2': '#202b42',
        'es-card':      'var(--bg-elevated)',
        'es-accent':    '#F26B25',
        'es-accent-h':  '#E85814',
        'es-basil':     '#4fbf9f',
        'es-gold':      '#f0b84a',
        'es-cream':     '#f1dcc0',
        'es-ink':       'var(--text-primary)',
        'es-ink-dim':   'var(--text-secondary)',
        'es-ink-mute':  'var(--text-muted)',
      },
      fontFamily: {
        sans:    ['Inter', '-apple-system', 'sans-serif'],
        display: ['Fraunces', 'serif'],
        serif:   ['Fraunces', 'serif'],
        mono:    ['"JetBrains Mono"', 'ui-monospace', 'monospace'],
      },
    },
  },
  plugins: [],
};
