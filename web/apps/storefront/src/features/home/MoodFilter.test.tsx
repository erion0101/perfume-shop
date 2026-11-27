import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { cardFixture, json, mockFetch, renderWithCart } from "@/test/utils";
import { MoodFilter } from "./MoodFilter";

describe("MoodFilter", () => {
  it("shows only moods with perfumes and loads the picked one", async () => {
    const calls = mockFetch({ "GET /api/catalog/fragrances": () => json({ items: [cardFixture({ id: "f2", slug: "rasasi-hawas", name: "Hawas for Him", mood: "Fresh" })], page: 1, pageSize: 6, totalCount: 1 }) });
    renderWithCart(<MoodFilter initial={[cardFixture()]} total={6} available={["Sweet", "Fresh"]} pageSize={6} />);

    const group = screen.getByRole("group", { name: "Drejtimi i aromës" });
    expect(within(group).getAllByRole("button").map((b) => b.textContent)).toEqual(["Të gjitha", "Ëmbël dhe vanilje", "Freski"]);
    expect(screen.getByText("6 aroma")).toBeInTheDocument();

    await userEvent.click(within(group).getByRole("button", { name: "Freski" }));
    expect(await screen.findByRole("heading", { name: "Hawas for Him" })).toBeInTheDocument();
    expect(screen.getByText("1 aromë")).toBeInTheDocument();
    expect(within(group).getByRole("button", { name: "Freski" })).toHaveAttribute("aria-pressed", "true");
    expect(calls[0]!.url.searchParams.get("Mood")).toBe("Fresh");
    expect(screen.getByRole("link", { name: "Shiko të gjitha parfumet" })).toHaveAttribute("href", "/parfumet?drejtimi=Fresh");

    await userEvent.click(within(group).getByRole("button", { name: "Të gjitha" }));
    expect(screen.getByRole("heading", { name: "Khamrah" })).toBeInTheDocument();
    expect(calls).toHaveLength(1); // "all" is the server-rendered list
  });
});
