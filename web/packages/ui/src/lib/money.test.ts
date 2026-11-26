import { describe, expect, it } from "vitest";
import { formatMoney, moneyToInput, parseMoney } from "./money";

describe("parseMoney", () => {
  it.each([
    ["39,90", 39.9],
    ["39.90", 39.9],
    ["40", 40],
    [" 12,5 € ", 12.5],
    ["1.299,00", 1299],
    ["1,299.00", 1299],
    ["0,01", 0.01],
  ])("reads %s as %s", (input, expected) => expect(parseMoney(input)).toBe(expected));

  it("returns null for empty and NaN for text", () => {
    expect(parseMoney("  ")).toBeNull();
    expect(parseMoney("abc")).toBeNaN();
    expect(parseMoney("-5")).toBeNaN();
  });
});

describe("formatting", () => {
  it("shows Albanian EUR with a decimal comma", () => {
    expect(formatMoney(39.9)).toBe("39,90\u00a0€");
    expect(formatMoney(1299)).toBe("1\u00a0299,00\u00a0€");
    expect(formatMoney(-5)).toBe("-5,00\u00a0€");
    expect(formatMoney(null)).toBe("—");
    expect(moneyToInput(39.9)).toBe("39,90");
    expect(moneyToInput(null)).toBe("");
  });
});
