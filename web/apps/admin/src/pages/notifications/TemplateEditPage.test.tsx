import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { adminSession, json, mockApi, problem, renderPage } from "@/test/utils";
import { TemplateEditPage } from "./TemplateEditPage";

const sms = (segments: number, encoding = "GSM-7") => ({ encoding, length: 120, segments, perSegment: encoding === "GSM-7" ? 160 : 70, nonGsmCharacters: "", warning: segments > 1 ? "2 SMS" : null });
const template = {
  key: "order-confirmed",
  channel: "Sms",
  audience: "Customer",
  alert: null,
  description: "Porosia u konfirmua",
  subject: null,
  body: "{{shopName}}: porosia {{orderNumber}} u konfirmua.",
  transliterate: true,
  isEnabled: true,
  placeholders: [
    { name: "shopName", sample: "PerfumeShop" },
    { name: "orderNumber", sample: "PS-2026-000042" },
  ],
  sms: sms(1),
  smsWithoutTransliteration: sms(1),
  updatedBy: null,
  updatedAtUtc: "2026-11-20T10:00:00Z",
};
const route = { path: "/notifications/templates/:key", route: "/notifications/templates/order-confirmed" };

function api(previewFor: (body: { body: string }) => Response) {
  return mockApi({
    "GET /api/admin/notifications/templates/{key}": () => json(template),
    "POST /api/admin/notifications/templates/{key}/preview": (_r, body) => previewFor(body as { body: string }),
    "PUT /api/admin/notifications/templates/{key}": (_r, body) => json({ ...template, ...(body as object), updatedAtUtc: "2026-11-26T10:00:00Z" }),
  });
}

const render = (body: { body: string }) =>
  json({ key: template.key, channel: "Sms", subject: null, text: body.body.replace("{{shopName}}", "PerfumeShop").replace("{{orderNumber}}", "PS-2026-000042"), usedPlaceholders: [], sms: sms(body.body.length > 60 ? 2 : 1) });

describe("TemplateEditPage", () => {
  it("previews the draft live, inserts placeholders and saves", async () => {
    const calls = api(render);
    renderPage(<TemplateEditPage />, route);

    expect(await screen.findByTestId("template-preview")).toHaveTextContent("PerfumeShop: porosia PS-2026-000042 u konfirmua.");
    expect(screen.getAllByText("1 SMS").length).toBeGreaterThan(0);

    const body = screen.getByLabelText(/Teksti/);
    await userEvent.clear(body);
    await userEvent.type(body, "Faleminderit! Porosia juaj do të niset sot dhe arrin brenda 1-2 ditëve. ");
    await userEvent.click(screen.getByRole("button", { name: "{{orderNumber}}" }));

    await waitFor(() => expect(screen.getByTestId("template-preview")).toHaveTextContent("PS-2026-000042"), { timeout: 2000 });
    expect(await screen.findByText("Më shumë se 1 SMS: kushton më shumë.")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Ruaj" }));
    await waitFor(() => expect(calls.find((c) => c.method === "PUT")?.body).toMatchObject({ body: expect.stringContaining("{{orderNumber}}"), transliterate: true, isEnabled: true, subject: null }));
  });

  it("shows unknown placeholders as an error and blocks saving", async () => {
    api((b) => (b.body.includes("{{iban}}") ? problem(400, "Notifications.Template.UnknownPlaceholder") : render(b)));
    renderPage(<TemplateEditPage />, route);

    const body = await screen.findByLabelText(/Teksti/);
    await userEvent.type(body, " {{{{iban}}");
    expect(await screen.findByText(/fushë të panjohur/, {}, { timeout: 2000 })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Ruaj" })).toBeDisabled();
  });

  it("is read-only for Staff", async () => {
    api(render);
    renderPage(<TemplateEditPage />, { ...route, session: { ...adminSession, roles: ["Staff"] } });

    expect(await screen.findByText("Vetëm administratori mund t'i ndryshojë shabllonet.")).toBeInTheDocument();
    expect(screen.getByLabelText(/Teksti/)).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Ruaj" })).not.toBeInTheDocument();
  });
});
