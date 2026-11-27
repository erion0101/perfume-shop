import { describe, expect, it } from "vitest";
import { accordName, formatSize, noteKey, noteName } from "./index";

describe("Albanian labels for imported data", () => {
  it("translates notes and accords, and keeps unknown ones as imported", () => {
    expect(noteName("Cinnamon")).toBe("kanellë");
    expect(noteName("Unobtainium")).toBe("Unobtainium");
    expect(accordName("warm spicy")).toBe("erëza të ngrohta");
  });

  it("maps a typed Albanian note back to the key the API filters on", () => {
    expect(noteKey("Vanilje")).toBe("vanilla");
    expect(noteKey(" kanellë ")).toBe("cinnamon");
    expect(noteKey("vanilla")).toBe("vanilla");
    expect(noteKey("qumësht dhie")).toBe("qumësht dhie");
  });

  it("formats sizes with the decimal comma", () => {
    expect(formatSize({ value: 100, unit: "Ml" })).toBe("100 ml");
    expect(formatSize({ value: 2.5, unit: "Gram" })).toBe("2,5 g");
    expect(formatSize(null)).toBeNull();
  });
});
