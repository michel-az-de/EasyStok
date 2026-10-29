// Ilustração original da assistente virtual; não representa uma cliente ou uma pessoa fotografada.
export function AvatarAssistente({ tamanho = 32, decorativo = false }) {
  return (
    <svg width={tamanho} height={tamanho} viewBox="0 0 48 48" fill="none"
      role={decorativo ? undefined : 'img'} aria-hidden={decorativo || undefined}
      aria-label={decorativo ? undefined : 'Assistente virtual, ilustração de uma senhora'}
      focusable="false" style={{ display: 'block', flexShrink: 0 }}>
      <title>Ilustração da assistente virtual</title>
      <circle cx="24" cy="24" r="23" fill="var(--papel)" />
      <path d="M8 46c1-9 7-14 16-14s15 5 16 14" fill="var(--marca-claro)" />
      <circle cx="24" cy="7" r="5" fill="var(--linha-media)" />
      <path d="M10 24c-1-9 4-16 14-16s15 7 14 16l-4 8H14Z" fill="var(--linha-media)" />
      <path d="M14 20c4-1 8-5 10-8 2 4 6 7 10 8v6c0 7-4 12-10 12s-10-5-10-12Z"
        fill="var(--papel)" stroke="var(--tinta-2)" strokeWidth="1.4" strokeLinejoin="round" />
      <g stroke="var(--tinta-2)" strokeWidth="1.5" strokeLinecap="round">
        <circle cx="19" cy="25" r="4" /><circle cx="29" cy="25" r="4" />
        <path d="M23 25h2M12 23l3 1m18 0 3-1M20 32c2 2 6 2 8 0M17 19l3-1m8 0 3 1" />
      </g>
      <circle cx="19" cy="25" r="1" fill="var(--tinta)" />
      <circle cx="29" cy="25" r="1" fill="var(--tinta)" />
    </svg>
  )
}
