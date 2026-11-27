import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { navigation } from "@/test/setup";
import { cardFixture, json, mockFetch } from "@/test/utils";
import { SearchBox } from "./SearchBox";

const suggestions = { query: "kamrah", fragrances: [cardFixture()], brands: [{ name: "Lattafa", slug: "lattafa", countryCode: "AE", logoUrl: null, fragranceCount: 4 }] };

describe("SearchBox", () => {
  it("debounces, lists suggestions and picks one with the keyboard", async () => {
    const calls = mockFetch({ "GET /api/catalog/search/suggestions": () => json(suggestions) });
    render(<SearchBox />);

    const box = screen.getByRole("combobox", { name: "Kërko parfum ose markë" });
    await userEvent.type(box, "kamrah");
    expect(await screen.findByRole("option", { name: /Lattafa Khamrah/ })).toBeInTheDocument();
    expect(calls).toHaveLength(1); // one request for the whole word, not one per letter
    expect(calls[0]!.url.searchParams.get("q")).toBe("kamrah");
    expect(box).toHaveAttribute("aria-expanded", "true");

    await userEvent.keyboard("{ArrowDown}{ArrowDown}");
    expect(screen.getByRole("option", { name: /Markat\s*Lattafa/ })).toHaveAttribute("aria-selected", "true");
    await userEvent.keyboard("{Enter}");
    expect(navigation.push).toHaveBeenCalledWith("/markat/lattafa");
  });

  it("Enter without a pick opens the full results; Escape closes the list", async () => {
    mockFetch({ "GET /api/catalog/search/suggestions": () => json({ ...suggestions, fragrances: [], brands: [] }) });
    render(<SearchBox />);

    const box = screen.getByRole("combobox");
    await userEvent.type(box, "zzz");
    expect((await screen.findAllByText("Asnjë sugjerim.")).length).toBeGreaterThan(0);
    await userEvent.keyboard("{Escape}");
    expect(box).toHaveAttribute("aria-expanded", "false");
    await userEvent.keyboard("{Enter}");
    expect(navigation.push).toHaveBeenCalledWith("/kerko?q=zzz");
  });

  it("does not search for a single letter", async () => {
    const calls = mockFetch({});
    render(<SearchBox />);
    await userEvent.type(screen.getByRole("combobox"), "k");
    await new Promise((r) => setTimeout(r, 300));
    expect(calls).toHaveLength(0);
  });
});
