import type { Metadata } from "next";

export const metadata: Metadata = { title: "Porosia" };

const fields = [
  { name: "fullName", label: "Emri dhe mbiemri", type: "text", autoComplete: "name" },
  { name: "phone", label: "Numri i telefonit", type: "tel", autoComplete: "tel" },
  { name: "city", label: "Qyteti", type: "text", autoComplete: "address-level2" },
  { name: "address", label: "Adresa", type: "text", autoComplete: "street-address" },
] as const;

/**
 * Checkout: Cash on Delivery ONLY. The customer pays the courier on delivery.
 * There is intentionally no card form.
 * TODO: phone OTP verification and submit to the Orders API.
 */
export default function CheckoutPage() {
  return (
    <main className="mx-auto max-w-xl p-6">
      <h1 className="text-2xl font-semibold">Porosia</h1>

      <form className="mt-6 space-y-4">
        {fields.map((field) => (
          <label key={field.name} className="block">
            <span className="text-sm font-medium">{field.label}</span>
            <input
              name={field.name}
              type={field.type}
              autoComplete={field.autoComplete}
              required
              className="mt-1 w-full rounded-md border px-3 py-2"
            />
          </label>
        ))}

        <label className="block">
          <span className="text-sm font-medium">Shënim (opsional)</span>
          <textarea name="note" rows={3} className="mt-1 w-full rounded-md border px-3 py-2" />
        </label>

        <p className="rounded-md bg-brand-50 p-3 text-sm">
          Pagesa bëhet me para në dorë kur ta pranoni porosinë.
        </p>

        <button type="button" disabled className="w-full rounded-md bg-oud px-4 py-2 text-white">
          Porosit (paguaj në dorëzim)
        </button>
      </form>
    </main>
  );
}
