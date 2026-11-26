/**
 * EUR amounts as shown to Kosovo staff: "39,90 €", "1 299,00 €" (decimal comma, non-breaking space for thousands).
 * Formatted by hand: browsers ship different locale data for "sq" (Chromium printed "€ 39.90").
 */
export function formatMoney(amount: number | null | undefined): string {
  if (amount == null || !Number.isFinite(amount)) {
    return "—";
  }

  const [whole, cents] = Math.abs(amount).toFixed(2).split(".") as [string, string];
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, "\u00a0");
  return `${amount < 0 ? "-" : ""}${grouped},${cents}\u00a0€`;
}

/**
 * Parses what staff type: "39,90", "39.90", "1.299,00", " 40 ". Returns null for empty input and NaN for garbage.
 * The last "," or "." with 1-2 digits after it is the decimal separator; other separators are thousands.
 */
export function parseMoney(input: string): number | null {
  const text = input.replace(/[\s€]/g, "");
  if (text === "") {
    return null;
  }

  if (!/^\d[\d.,]*$/.test(text)) {
    return Number.NaN;
  }

  const match = /^(.*?)[.,](\d{1,2})$/.exec(text);
  const whole = (match ? match[1]! : text).replace(/[.,]/g, "");
  const value = Number(match ? `${whole}.${match[2]}` : whole);
  return Number.isFinite(value) ? Math.round(value * 100) / 100 : Number.NaN;
}

/** For an input field: "39,90" (no currency sign, Albanian decimal comma). */
export function moneyToInput(amount: number | null | undefined): string {
  return amount == null ? "" : amount.toFixed(2).replace(".", ",");
}
