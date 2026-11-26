import { Select, SelectField } from "@perfumeshop/ui";
import { useState } from "react";
import { useFragrance, useFragrances } from "@/features/catalog/api";
import { label, t } from "@/i18n";
import { SearchInput } from "./SearchInput";

/** Find the fragrance, then pick one of its variants. Reports the variant id (or "" while nothing is chosen). */
export function VariantPicker({
  value,
  onChange,
  error,
  index,
  initialFragranceId = "",
}: {
  value: string;
  onChange: (variantId: string) => void;
  error?: string;
  index: number;
  initialFragranceId?: string;
}) {
  const [search, setSearch] = useState("");
  const [fragranceId, setFragranceId] = useState(initialFragranceId);
  const fragrances = useFragrances({ search, brandId: "", status: "", inStock: "", sort: "name", page: 1, pageSize: 20 });
  const fragrance = useFragrance(fragranceId);
  const variants = fragranceId ? (fragrance.data?.variants ?? []) : [];

  return (
    <div className="grid gap-2 sm:grid-cols-2">
      <div className="grid gap-1.5">
        <span className="text-sm font-medium">{t.receive.fragranceSearch}</span>
        <SearchInput value={search} onChange={setSearch} className="sm:max-w-none" />
        <Select
          aria-label={`${t.receive.fragranceSearch} ${index + 1}`}
          value={fragranceId}
          onChange={(e) => {
            setFragranceId(e.target.value);
            onChange("");
          }}
        >
          <option value="">{(fragrances.data?.items.length ?? 0) === 0 ? t.receive.noFragrance : t.common.choose}</option>
          {fragrance.data && !(fragrances.data?.items ?? []).some((f) => f.id === fragranceId) ? (
            <option value={fragrance.data.id}>
              {fragrance.data.brand.name} {fragrance.data.name}
            </option>
          ) : null}
          {(fragrances.data?.items ?? []).map((f) => (
            <option key={f.id} value={f.id}>
              {f.brandName} {f.name}
            </option>
          ))}
        </Select>
      </div>
      <SelectField
        label={t.receive.chooseVariant}
        aria-label={`${t.receive.variant} ${index + 1}`}
        placeholder={t.common.choose}
        required
        error={error}
        disabled={!fragranceId}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        options={variants.map((v) => ({
          value: v.id,
          label: `${v.sku} · ${label(t.enums.variantType, v.type)}${v.size ? ` ${v.size.value} ${label(t.enums.sizeUnit, v.size.unit)}` : ""}${v.isActive ? "" : ` (${t.variants.inactive})`}`,
        }))}
      />
    </div>
  );
}
