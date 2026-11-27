import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { json, mockApi, problem, renderPage } from "@/test/utils";
import { FragranceEditPage } from "./FragranceEditPage";

const fragrance = {
  id: "f1",
  brand: { id: "b1", name: "Lattafa", slug: "lattafa" },
  name: "Khamrah",
  slug: "lattafa-khamrah",
  status: "Draft",
  concentration: "EauDeParfum",
  gender: "Unisex",
  olfactiveFamily: "Amber",
  launchYear: 2022,
  perfumers: [],
  description: null,
  notes: { top: [{ name: "Cinnamon", imageUrl: null }], heart: [{ name: "Dates", imageUrl: null }], base: [{ name: "Vanilla", imageUrl: null }] },
  accords: [{ name: "warm spicy", percentage: 100 }],
  longevity: "LongLasting",
  sillage: "Strong",
  seasons: ["Winter"],
  occasions: ["Night"],
  similarTo: [],
  fragellaId: "fg-1",
  fragellaImportedAtUtc: "2026-11-26T10:00:00Z",
  variants: [],
  createdAtUtc: "2026-11-26T10:00:00Z",
  updatedAtUtc: "2026-11-26T10:00:00Z",
  moodOverride: "Oud",
  mood: "Oud",
};

describe("FragranceEditPage", () => {
  it("shows the note pyramid and accords", async () => {
    mockApi({ "GET /api/admin/catalog/fragrances/{id}": () => json(fragrance) });
    renderPage(<FragranceEditPage />, { path: "/catalog/fragrances/:id", route: "/catalog/fragrances/f1" });

    expect(await screen.findByRole("heading", { name: "Lattafa Khamrah" })).toBeInTheDocument();
    expect(screen.getByText("Cinnamon")).toBeInTheDocument();
    expect(screen.getByText("Vanilla")).toBeInTheDocument();
    expect(screen.getByText("warm spicy")).toBeInTheDocument();
    expect(screen.getByText("Ende pa variante. Shtoni madhësinë që shitni (p.sh. 100 ml).")).toBeInTheDocument();
  });

  it("shows the domain error when publishing without an active variant", async () => {
    mockApi({
      "GET /api/admin/catalog/fragrances/{id}": () => json(fragrance),
      "POST /api/admin/catalog/fragrances/{id}/publish": () => problem(409, "Catalog.Fragrance.PublishRequiresActiveVariant"),
    });
    renderPage(<FragranceEditPage />, { path: "/catalog/fragrances/:id", route: "/catalog/fragrances/f1" });

    await userEvent.click(await screen.findByRole("button", { name: "Publiko" }));
    await userEvent.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Publiko" }));

    expect(await screen.findByText("Për ta publikuar duhet të paktën një variant aktiv me çmim.")).toBeInTheDocument();
  });

  it("saves edited notes and accords with the full fragrance", async () => {
    const calls = mockApi({
      "GET /api/admin/catalog/fragrances/{id}": () => json(fragrance),
      "PUT /api/admin/catalog/fragrances/{id}": (_, body) => json({ ...fragrance, ...(body as object) }),
    });
    renderPage(<FragranceEditPage />, { path: "/catalog/fragrances/:id", route: "/catalog/fragrances/f1" });

    await userEvent.click(await screen.findByRole("button", { name: "Ndrysho notat dhe akordet" }));
    const form = screen.getByRole("form", { name: "Notat dhe akordet" });
    const top = within(form).getByLabelText(/Notat e sipërme/);
    await userEvent.clear(top);
    await userEvent.type(top, "Cinnamon, Cardamom");
    await userEvent.click(within(form).getByRole("button", { name: "Ruaj" }));

    const put = await screen.findByText("Cardamom").then(() => calls.find((c) => c.method === "PUT")!);
    expect(put.body).toMatchObject({
      name: "Khamrah",
      seasons: ["Winter"],
      notes: { top: [{ name: "Cinnamon", imageUrl: null }, { name: "Cardamom", imageUrl: null }], base: [{ name: "Vanilla", imageUrl: null }] },
      accords: [{ name: "warm spicy", percentage: 100 }],
      moodOverride: "Oud", // kept: saving the notes must not clear the admin's mood choice
    });
  });

  it("sets the shop mood back to automatic", async () => {
    const calls = mockApi({
      "GET /api/admin/catalog/fragrances/{id}": () => json(fragrance),
      "PUT /api/admin/catalog/fragrances/{id}": (_, body) => json({ ...fragrance, ...(body as object), moodOverride: null, mood: "Spicy" }),
    });
    renderPage(<FragranceEditPage />, { path: "/catalog/fragrances/:id", route: "/catalog/fragrances/f1" });

    const mood = await screen.findByLabelText(/Drejtimi në dyqan/);
    expect(mood).toHaveValue("Oud");
    await userEvent.selectOptions(mood, "");
    await userEvent.click(within(screen.getByRole("form", { name: "Të dhënat" })).getByRole("button", { name: "Ruaj" }));

    await waitFor(() => expect(calls.find((c) => c.method === "PUT")?.body).toMatchObject({ moodOverride: null }));
    expect(await screen.findByRole("option", { name: "Automatik (Erëza dhe duhan)" })).toBeInTheDocument();
  });
});
