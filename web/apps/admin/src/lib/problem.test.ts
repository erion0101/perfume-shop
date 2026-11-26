import { describe, expect, it } from "vitest";
import { t } from "@/i18n";
import { ApiError, applyFieldErrors, describeError, toFieldPath } from "./problem";

describe("describeError", () => {
  it("translates known API codes", () => {
    expect(describeError(new ApiError(409, { code: "Catalog.Fragrance.PublishRequiresActiveVariant" }))).toBe(
      "Për ta publikuar duhet të paktën një variant aktiv me çmim.",
    );
  });

  it("lists invalid fields in Albanian for validation problems", () => {
    const error = new ApiError(400, { errors: { Sku: ["'Sku' must not be empty."], "Lines[0].Quantity": ["x"], "Lines[1].Quantity": ["y"] } });
    expect(describeError(error)).toBe("Kontrolloni: SKU, Sasia.");
  });

  it.each([
    [0, t.httpErrors.network],
    [401, t.httpErrors.unauthorized],
    [403, t.httpErrors.forbidden],
    [404, t.httpErrors.notFound],
    [409, t.httpErrors.conflict],
    [429, t.httpErrors.tooMany],
    [503, t.httpErrors.unavailable],
    [500, t.httpErrors.server],
  ])("falls back to the status (%i)", (status, message) => {
    expect(describeError(new ApiError(status, { code: "Some.Unknown.Code" }))).toBe(message);
  });

  it("never shows raw English server text", () => {
    expect(describeError(new Error("TypeError: x is undefined"))).toBe(t.httpErrors.unknown);
  });
});

describe("field errors", () => {
  it("maps API keys to form paths", () => {
    expect(toFieldPath("Lines[2].UnitCost")).toBe("lines.2.unitCost");
    expect(toFieldPath("Size.Value")).toBe("size.value");
  });

  it("puts server errors on known form fields only", () => {
    const set: string[] = [];
    const applied = applyFieldErrors(new ApiError(400, { errors: { Sku: ["taken"], Unknown: ["x"] } }), (path) => set.push(path), ["sku", "ean"]);
    expect(applied).toBe(true);
    expect(set).toEqual(["sku"]);
  });
});
