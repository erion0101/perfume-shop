import { sq } from "./sq";

/** The shop speaks Albanian. English / Macedonian later: add en.ts (`satisfies Texts`) and choose here. */
export const t = sq;
export type { Texts } from "./sq";

/** Albanian label for an API enum value, falling back to the value itself. */
export function label(map: Record<string, string>, value: string | null | undefined): string {
  return value ? (map[value] ?? value) : t.common.none;
}

/** "cinnamon" → "kanellë"; notes we have no translation for are shown as imported. */
export function noteName(name: string): string {
  return t.notes[name.trim().toLowerCase()] ?? name;
}

/** Albanian (or English) note as typed → the imported key the API filters on: "Vanilje" → "vanilla". */
export function noteKey(input: string): string {
  const text = input.trim().toLocaleLowerCase("sq");
  if (!text || t.notes[text]) {
    return text;
  }

  const match = Object.entries(t.notes).find(([, albanian]) => albanian === text);
  return match ? match[0] : text;
}

/** "warm spicy" → "erëza të ngrohta"; unknown accords as imported. */
export function accordName(name: string): string {
  return t.accords[name.trim().toLowerCase()] ?? name;
}

/** First letter upper case (note names in sentences and chips). */
export function capitalize(text: string): string {
  return text ? text.charAt(0).toLocaleUpperCase("sq") + text.slice(1) : text;
}

/** "100 ml", "50 g", "1 copë"; null when the variant has no size (sets). Decimal comma like prices. */
export function formatSize(size: { value: number; unit: string } | null | undefined): string | null {
  if (!size) {
    return null;
  }

  const value = Number.isInteger(size.value) ? String(size.value) : String(size.value).replace(".", ",");
  return `${value} ${label(t.enums.sizeUnit, size.unit)}`;
}
