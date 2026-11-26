import { sq } from "./sq";

/** The admin panel speaks Albanian. English: add en.ts (`satisfies Texts`) and choose here. */
export const t = sq;
export type { Texts } from "./sq";

/** Label for an API enum value, falling back to the value itself. */
export function label(map: Record<string, string>, value: string | null | undefined): string {
  return value ? (map[value] ?? value) : t.common.none;
}

export function options(map: Record<string, string>) {
  return Object.entries(map).map(([value, text]) => ({ value, label: text }));
}

const zone = "Europe/Belgrade";

/** dd.MM.yyyy and dd.MM.yyyy HH:mm in Kosovo time, the same in every browser (no reliance on "sq" locale data). */
function parts(iso: string, withTime: boolean) {
  const values = Object.fromEntries(
    new Intl.DateTimeFormat("en-GB", { timeZone: zone, day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit", hourCycle: "h23" })
      .formatToParts(new Date(iso))
      .map((p) => [p.type, p.value]),
  );
  return `${values.day}.${values.month}.${values.year}${withTime ? ` ${values.hour}:${values.minute}` : ""}`;
}

/** Dates without time (yyyy-MM-dd from the API) are shown as they are, without a time-zone shift. */
export const formatDate = (iso: string | null | undefined) =>
  !iso ? t.common.none : /^\d{4}-\d{2}-\d{2}$/.test(iso) ? iso.split("-").reverse().join(".") : parts(iso, false);

export const formatDateTime = (iso: string | null | undefined) => (iso ? parts(iso, true) : t.common.none);
