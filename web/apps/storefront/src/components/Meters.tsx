import { accordName, label, t } from "@/i18n";

const longevityScale = ["VeryWeak", "Weak", "Moderate", "LongLasting", "Eternal"];
const sillageScale = ["Intimate", "Moderate", "Strong", "Enormous"];

function Meter({ name, value, text }: { name: string; value: number; text: string }) {
  const percent = Math.round(value * 100);
  return (
    <div>
      <dt className="mb-2 flex justify-between gap-3 text-sm text-soft">
        <span>{name}</span>
        <span className="text-muted">{text}</span>
      </dt>
      <dd className="m-0 h-1.5 rounded-[3px] bg-line" role="meter" aria-label={t.product.meter(name, text)} aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent}>
        <div className="h-1.5 rounded-[3px] bg-gold" style={{ width: `${percent}%` }} />
      </dd>
    </div>
  );
}

/** Longevity and sillage as the thin gold bars of the "Aroma e javës" section. */
export function ProfileMeters({ longevity, sillage, className }: { longevity?: string | null; sillage?: string | null; className?: string }) {
  const l = longevity ? longevityScale.indexOf(longevity) : -1;
  const s = sillage ? sillageScale.indexOf(sillage) : -1;
  if (l < 0 && s < 0) {
    return null;
  }

  return (
    <dl className={className ?? "grid max-w-[440px] gap-[18px]"}>
      {l >= 0 ? <Meter name={t.product.longevity} value={(l + 1) / longevityScale.length} text={label(t.enums.longevity, longevity)} /> : null}
      {s >= 0 ? <Meter name={t.product.sillage} value={(s + 1) / sillageScale.length} text={label(t.enums.sillage, sillage)} /> : null}
    </dl>
  );
}

/** Main accords as horizontal bars, strongest first (a list, so screen readers hear name and percentage). */
export function AccordBars({ accords }: { accords: { name: string; percentage: number }[] }) {
  if (accords.length === 0) {
    return null;
  }

  return (
    <ul className="m-0 grid list-none gap-3 p-0" aria-label={t.product.accords}>
      {accords.slice(0, 8).map((a) => (
        <li key={a.name} className="grid grid-cols-[minmax(7rem,9rem)_1fr] items-center gap-3 text-sm">
          <span className="text-soft first-letter:uppercase">{accordName(a.name)}</span>
          <span className="h-2 rounded-full bg-line" aria-label={t.product.accordValue(accordName(a.name), a.percentage)} role="img">
            <span className="block h-2 rounded-full bg-gold" style={{ width: `${Math.max(4, Math.min(100, a.percentage))}%` }} />
          </span>
        </li>
      ))}
    </ul>
  );
}
