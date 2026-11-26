import { useId, useState, type ReactNode } from "react";
import { cn } from "../lib/cn";
import { useUiTexts } from "../lib/texts";

export interface ChartDatum {
  key: string;
  /** Category label (axis / row), e.g. "26.11" or "Lattafa Khamrah". */
  label: string;
  value: number;
  /** Text for the tooltip and the table, e.g. "12 porosi · 503,80 €". Defaults to the value. */
  detail?: string;
}

interface ChartBaseProps {
  /** The chart's title names the single series (no legend box needed for one series). */
  title: string;
  data: ChartDatum[];
  /** Formats a value for labels and axis ticks. */
  format?: (value: number) => string;
  /** Click / Enter on a mark (e.g. open the filtered list behind it). */
  onSelect?: (datum: ChartDatum) => void;
  className?: string;
}

const fmt = (v: number) => String(v);

/** Clean axis maximum: 1, 2, 5 × 10ⁿ. */
function niceMax(max: number) {
  if (max <= 0) {
    return 1;
  }

  const power = 10 ** Math.floor(Math.log10(max));
  return ([1, 2, 5, 10].find((m) => m * power >= max) ?? 10) * power;
}

function DataTable({ data, format, label }: { data: ChartDatum[]; format: (v: number) => string; label: string }) {
  return (
    <table className="w-full text-sm" aria-label={label}>
      <tbody>
        {data.map((d) => (
          <tr key={d.key} className="border-b border-border last:border-0">
            <th scope="row" className="py-1 pr-2 text-left font-normal">
              {d.label}
            </th>
            <td className="py-1 text-right tabular-nums">{d.detail ?? format(d.value)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function ChartFrame({ title, children, data, format }: { title: string; children: ReactNode; data: ChartDatum[]; format: (v: number) => string }) {
  const texts = useUiTexts();
  const [asTable, setAsTable] = useState(false);
  return (
    <figure className="grid gap-2">
      <figcaption className="flex items-center justify-between gap-2">
        <span className="text-sm font-medium">{title}</span>
        <button type="button" className="text-xs text-muted-foreground underline-offset-4 hover:underline" aria-pressed={asTable} onClick={() => setAsTable(!asTable)}>
          {asTable ? texts.chartAsChart : texts.chartAsTable}
        </button>
      </figcaption>
      {asTable ? <DataTable data={data} format={format} label={title} /> : children}
    </figure>
  );
}

/**
 * Columns over time (one series): 4px rounded caps on a shared baseline, ≤ 24px wide with air around them,
 * recessive hairline grid, the latest and the highest value labelled, every value in the hover/focus tooltip.
 */
export function ColumnChart({ title, data, format = fmt, onSelect, className, height = 160 }: ChartBaseProps & { height?: number }) {
  const [active, setActive] = useState<string | null>(null);
  const id = useId();
  const max = niceMax(Math.max(0, ...data.map((d) => d.value)));
  const peak = data.reduce<ChartDatum | null>((best, d) => (!best || d.value > best.value ? d : best), null);
  const last = data.at(-1);

  return (
    <ChartFrame title={title} data={data} format={format}>
      <div className={cn("relative", className)}>
        <div className="flex gap-2">
          <div className="flex flex-col justify-between py-0 text-right text-[11px] tabular-nums text-muted-foreground" style={{ height }} aria-hidden>
            <span>{format(max)}</span>
            <span>{format(max / 2)}</span>
            <span>0</span>
          </div>
          <div className="relative flex-1">
            {[0, 0.5, 1].map((f) => (
              <div key={f} className="pointer-events-none absolute inset-x-0 border-t border-border" style={{ top: f * height }} aria-hidden />
            ))}
            <ul className="relative flex items-end" style={{ height }} aria-label={title}>
              {data.map((d) => {
                const h = Math.max(d.value > 0 ? 2 : 0, (d.value / max) * height);
                const labelled = d === last || d === peak;
                const tip = `${d.label}: ${d.detail ?? format(d.value)}`;
                return (
                  <li key={d.key} className="relative flex h-full flex-1 items-end justify-center px-[1px]">
                    <button
                      type="button"
                      aria-label={tip}
                      aria-describedby={active === d.key ? `${id}-tip` : undefined}
                      className="group flex h-full w-full items-end justify-center focus-visible:outline-none"
                      onMouseEnter={() => setActive(d.key)}
                      onMouseLeave={() => setActive(null)}
                      onFocus={() => setActive(d.key)}
                      onBlur={() => setActive(null)}
                      onClick={onSelect ? () => onSelect(d) : undefined}
                    >
                      <span
                        className={cn("block w-full max-w-6 rounded-t-[4px] bg-chart-1 transition-opacity", active && active !== d.key && "opacity-60", "group-focus-visible:ring-2 group-focus-visible:ring-ring")}
                        style={{ height: h }}
                      />
                    </button>
                    {labelled && d.value > 0 ? (
                      <span className="pointer-events-none absolute text-[11px] tabular-nums text-foreground" style={{ bottom: h + 2 }} aria-hidden>
                        {format(d.value)}
                      </span>
                    ) : null}
                    {active === d.key ? (
                      <span id={`${id}-tip`} role="tooltip" className="pointer-events-none absolute z-10 whitespace-nowrap rounded-md border border-border bg-background px-2 py-1 text-xs shadow-md" style={{ bottom: h + 18 }}>
                        {tip}
                      </span>
                    ) : null}
                  </li>
                );
              })}
            </ul>
          </div>
        </div>
        <div className="ml-8 flex text-[11px] text-muted-foreground" aria-hidden>
          {data.map((d, i) => (
            <span key={d.key} className="flex-1 truncate text-center">
              {i % 2 === data.length % 2 ? d.label : ""}
            </span>
          ))}
        </div>
      </div>
    </ChartFrame>
  );
}

/** Horizontal bars for a ranked list (one series): label on the left, value at the bar's tip. */
export function BarList({ title, data, format = fmt, onSelect, className }: ChartBaseProps) {
  const max = niceMax(Math.max(0, ...data.map((d) => d.value)));
  return (
    <ChartFrame title={title} data={data} format={format}>
      <ul className={cn("grid gap-2", className)} aria-label={title}>
        {data.map((d) => {
          const content = (
            <>
              <span className="truncate text-left text-sm">{d.label}</span>
              <span className="flex items-center gap-2">
                <span className="h-3 rounded-r-[4px] bg-chart-1" style={{ width: `${Math.max(1, (d.value / max) * 100)}%`, maxWidth: "calc(100% - 4rem)" }} />
                <span className="shrink-0 text-xs tabular-nums">{d.detail ?? format(d.value)}</span>
              </span>
            </>
          );
          return (
            <li key={d.key}>
              {onSelect ? (
                <button type="button" title={`${d.label}: ${d.detail ?? format(d.value)}`} className="grid w-full grid-cols-[minmax(0,10rem)_1fr] items-center gap-2 rounded-md px-1 py-0.5 hover:bg-muted" onClick={() => onSelect(d)}>
                  {content}
                </button>
              ) : (
                <div className="grid grid-cols-[minmax(0,10rem)_1fr] items-center gap-2 px-1 py-0.5">{content}</div>
              )}
            </li>
          );
        })}
      </ul>
    </ChartFrame>
  );
}

/** A headline number (not a chart). Wrap it in a link to the list behind it. */
export function StatTile({ label, value, hint, tone = "neutral" }: { label: string; value: ReactNode; hint?: ReactNode; tone?: "neutral" | "warning" | "danger" | "success" }) {
  return (
    <div
      className={cn(
        "grid h-full gap-1 rounded-lg border bg-background p-3",
        tone === "danger" ? "border-red-300 bg-red-50" : tone === "warning" ? "border-amber-300 bg-amber-50" : tone === "success" ? "border-emerald-200" : "border-border",
      )}
    >
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-2xl font-semibold tabular-nums">{value}</span>
      {hint ? <span className="text-xs text-muted-foreground">{hint}</span> : null}
    </div>
  );
}
