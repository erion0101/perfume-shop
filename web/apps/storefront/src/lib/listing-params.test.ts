import { describe, expect, it } from "vitest";
import { hasFilters, parseFilters, toSearch } from "./listing-params";

describe("listing URL filters", () => {
  it("round-trips the Albanian keys", () => {
    const filters = parseFilters(new URLSearchParams("marka=lattafa&drejtimi=Sweet&nota=vanilla&cmimi-nga=20&cmimi-deri=39,90&ne-stok=po&rendit=price_asc&faqja=2"));
    expect(filters).toMatchObject({ brand: ["lattafa"], mood: "Sweet", note: "vanilla", minPrice: "20", maxPrice: "39.90", inStock: true, sort: "price_asc", page: 2 });
    expect(toSearch(filters)).toBe("marka=lattafa&drejtimi=Sweet&nota=vanilla&cmimi-nga=20&cmimi-deri=39.90&ne-stok=po&rendit=price_asc&faqja=2");
  });

  it("drops values the API would reject instead of failing the page", () => {
    const filters = parseFilters({ drejtimi: "Salty", per: "Robot", "cmimi-nga": "abc", rendit: "random", faqja: "-4", marka: "<script>" });
    expect(filters).toMatchObject({ mood: "", gender: "", minPrice: "", sort: "", page: 1, brand: [] });
    expect(hasFilters(filters)).toBe(false);
    expect(toSearch(filters)).toBe("");
  });

  it("leaves page 1 out of links", () => {
    expect(toSearch({ mood: "Oud", page: 1 })).toBe("drejtimi=Oud");
  });
});
