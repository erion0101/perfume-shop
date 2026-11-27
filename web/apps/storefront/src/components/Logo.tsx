/** The arch + sun + dunes mark from the approved design. Decorative: the wordmark next to it is the name. */
export function LogoMark({ size = 34 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 34 34" aria-hidden="true" focusable="false">
      <path d="M3 31V17a14 14 0 0 1 28 0v14" fill="none" stroke="#C8A15A" strokeWidth="1.6" />
      <circle cx="17" cy="14" r="4" fill="#C8A15A" />
      <path d="M6 31c5-6 9-8 11-8s7 2 11 8" fill="none" stroke="#C8A15A" strokeWidth="1.6" />
    </svg>
  );
}
