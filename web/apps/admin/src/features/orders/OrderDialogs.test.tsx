import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { courier, orderAdmin } from "@/test/fixtures";
import { json, mockApi, problem, renderPage } from "@/test/utils";
import { CreateShipmentDialog } from "./OrderDialogs";

const couriers = [
  courier(),
  courier({ id: "k2", name: "Docs Express", acceptsFlammableGoods: false, isSelectable: false }),
  courier({ id: "k3", name: "Albania Only", enabledCountries: ["AL"] }),
  courier({ id: "k4", name: "Disabled", isEnabled: false }),
];

describe("CreateShipmentDialog", () => {
  it("offers only couriers that accept UN1266 and deliver to the order's country", async () => {
    mockApi({ "GET /api/admin/shipping/couriers": () => json(couriers) });
    renderPage(<CreateShipmentDialog order={orderAdmin()} open onOpenChange={() => {}} onCreated={() => {}} />);

    const select = await screen.findByLabelText(/^Korrieri/);
    await waitFor(() => expect(within(select).getAllByRole("option").map((o) => o.textContent)).toEqual(["Zgjidhni…", "Posta e Kosovës"]));
    expect(select).toHaveValue("k1"); // the only valid one is preselected
  });

  it("explains when no courier can take the parcel", async () => {
    mockApi({ "GET /api/admin/shipping/couriers": () => json([couriers[1]]) });
    renderPage(<CreateShipmentDialog order={orderAdmin()} open onOpenChange={() => {}} onCreated={() => {}} />);

    expect(await screen.findByText(/Nuk ka korrier të vlefshëm/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Krijo dërgesën" })).toBeDisabled();
  });

  it("creates the shipment and hands it back for printing", async () => {
    const onCreated = vi.fn();
    const shipment = { id: "s1", trackingNumber: "PK1XK" };
    const calls = mockApi({
      "GET /api/admin/shipping/couriers": () => json(couriers),
      "POST /api/admin/shipping/shipments": () => json(shipment, 201),
    });
    renderPage(<CreateShipmentDialog order={orderAdmin()} open onOpenChange={() => {}} onCreated={onCreated} />);

    const dialog = await screen.findByRole("dialog");
    await userEvent.type(within(dialog).getByLabelText(/Nr. gjurmimit/), "PK1XK");
    await userEvent.type(within(dialog).getByLabelText(/Pesha/), "450");
    await userEvent.click(within(dialog).getByRole("button", { name: "Krijo dërgesën" }));

    await waitFor(() => expect(onCreated).toHaveBeenCalledWith(shipment));
    expect(calls.find((c) => c.method === "POST")!.body).toEqual({ orderId: "o1", courierId: "k1", trackingNumber: "PK1XK", parcelCount: 1, weightGrams: 450, note: null });
  });

  it("shows a taken tracking number in Albanian", async () => {
    mockApi({ "GET /api/admin/shipping/couriers": () => json(couriers), "POST /api/admin/shipping/shipments": () => problem(409, "Shipping.Shipment.TrackingTaken") });
    renderPage(<CreateShipmentDialog order={orderAdmin()} open onOpenChange={() => {}} onCreated={() => {}} />);

    const dialog = await screen.findByRole("dialog");
    await userEvent.type(within(dialog).getByLabelText(/Nr. gjurmimit/), "PK1XK");
    await userEvent.click(within(dialog).getByRole("button", { name: "Krijo dërgesën" }));

    expect(await screen.findByText("Ky numër gjurmimi përdoret tashmë.")).toBeInTheDocument();
  });
});
