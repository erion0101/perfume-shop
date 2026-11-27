"use client";

import { cn } from "@perfumeshop/ui";
import { usePathname, useRouter } from "next/navigation";
import { useOptimistic, useState, useTransition, type ReactNode } from "react";
import { Drawer } from "@/components/Drawer";
import { capitalize, label, noteKey, noteName, t } from "@/i18n";
import { GENDERS, hasFilters, toSearch, type ListingFilters } from "@/lib/listing-params";

type Facet = { value: string; label: string; count: number };
export interface FacetLists {
  brands: Facet[];
  moods: Facet[];
  families: Facet[];
  notes: Facet[];
  minPrice: number | null;
  maxPrice: number | null;
}

/**
 * Filters as shown (optimistic: a tapped control is checked at once, before the server answers) and `go` to put
 * new filters in the URL, which re-renders the results on the server.
 */
function useNavigateFilters(filters: ListingFilters) {
  const router = useRouter();
  const pathname = usePathname();
  const [pending, startTransition] = useTransition();
  const [shown, setShown] = useOptimistic(filters);
  const go = (next: ListingFilters) => {
    const query = toSearch({ ...next, page: 1 });
    startTransition(() => {
      setShown(next);
      router.replace(query ? `${pathname}?${query}` : pathname, { scroll: false });
    });
  };
  return { filters: shown, go, pending };
}

function Group({ legend, children }: { legend: string; children: ReactNode }) {
  return (
    <fieldset className="m-0 grid gap-2.5 border-0 border-b border-line p-0 pb-6">
      <legend className="mb-3 p-0 text-sm font-medium text-soft">{legend}</legend>
      {children}
    </fieldset>
  );
}

const field = "min-h-12 w-full rounded-xl border border-field bg-ground px-4 text-base text-text";

function Choice({ name, value, checked, onChange, children }: { name: string; value: string; checked: boolean; onChange: () => void; children: ReactNode }) {
  return (
    <label className={cn("flex min-h-11 cursor-pointer items-center gap-3 rounded-lg px-1 text-[15px]", checked ? "text-gold-light" : "text-text")}>
      <input type="radio" name={name} value={value} checked={checked} onChange={onChange} className="size-5 accent-[#C8A15A]" />
      {children}
    </label>
  );
}

/** Remounts the form when the URL's typed values change (back button, chip removed), so its inputs follow. */
export function FilterForm(props: { filters: ListingFilters; facets: FacetLists; idPrefix: string }) {
  const f = props.filters;
  return <FilterFormInner key={`${f.minPrice}|${f.maxPrice}|${f.note}`} {...props} />;
}

/** The filter form; changes go to the URL right away (the server re-renders the results). */
function FilterFormInner({ filters: fromUrl, facets, idPrefix }: { filters: ListingFilters; facets: FacetLists; idPrefix: string }) {
  const { filters, go, pending } = useNavigateFilters(fromUrl);
  const [min, setMin] = useState(filters.minPrice);
  const [max, setMax] = useState(filters.maxPrice);
  const [note, setNote] = useState(filters.note ? capitalize(noteName(filters.note)) : "");

  const set = (change: Partial<ListingFilters>) => go({ ...filters, ...change });
  const id = (name: string) => `${idPrefix}-${name}`;

  return (
    <form
      aria-busy={pending}
      className="grid gap-6"
      onSubmit={(e) => {
        e.preventDefault();
        set({ minPrice: min.trim(), maxPrice: max.trim(), note: noteKey(note) });
      }}
    >
      <label className="flex min-h-11 cursor-pointer items-center gap-3 text-[15px]">
        <input type="checkbox" checked={filters.inStock} onChange={(e) => set({ inStock: e.target.checked })} className="size-5 accent-[#C8A15A]" />
        {t.listing.inStock}
      </label>

      {facets.moods.length > 0 ? (
        <Group legend={t.listing.mood}>
          <Choice name={id("mood")} value="" checked={!filters.mood} onChange={() => set({ mood: "" })}>
            {t.home.allMoods}
          </Choice>
          {facets.moods.map((m) => (
            <Choice key={m.value} name={id("mood")} value={m.value} checked={filters.mood === m.value} onChange={() => set({ mood: m.value })}>
              {label(t.enums.mood, m.value)} <span className="text-subtle">({m.count})</span>
            </Choice>
          ))}
        </Group>
      ) : null}

      <Group legend={t.listing.brand}>
        <select id={id("brand")} aria-label={t.listing.brand} className={field} value={filters.brand[0] ?? ""} onChange={(e) => set({ brand: e.target.value ? [e.target.value] : [] })}>
          <option value="">{t.listing.all}</option>
          {facets.brands.map((b) => (
            <option key={b.value} value={b.value}>
              {b.label} ({b.count})
            </option>
          ))}
        </select>
      </Group>

      <Group legend={t.listing.gender}>
        <Choice name={id("gender")} value="" checked={!filters.gender} onChange={() => set({ gender: "" })}>
          {t.listing.all}
        </Choice>
        {GENDERS.map((g) => (
          <Choice key={g} name={id("gender")} value={g} checked={filters.gender === g} onChange={() => set({ gender: g })}>
            {label(t.enums.gender, g)}
          </Choice>
        ))}
      </Group>

      {facets.families.length > 0 ? (
        <Group legend={t.listing.family}>
          <select id={id("family")} aria-label={t.listing.family} className={field} value={filters.family} onChange={(e) => set({ family: e.target.value })}>
            <option value="">{t.listing.all}</option>
            {facets.families.map((f) => (
              <option key={f.value} value={f.value}>
                {label(t.enums.family, f.value)} ({f.count})
              </option>
            ))}
          </select>
        </Group>
      ) : null}

      <Group legend={t.listing.note}>
        <input id={id("note")} list={id("notes")} aria-label={t.listing.note} placeholder={t.listing.notePlaceholder} className={field} value={note} onChange={(e) => setNote(e.target.value)} onBlur={() => noteKey(note) !== filters.note && set({ note: noteKey(note) })} />
        <datalist id={id("notes")}>
          {facets.notes.map((n) => (
            <option key={n.value} value={capitalize(noteName(n.value))} />
          ))}
        </datalist>
      </Group>

      <Group legend={t.listing.price}>
        <div className="grid grid-cols-2 gap-3">
          <label className="grid gap-1.5 text-sm text-soft">
            {t.listing.priceMin}
            <input inputMode="decimal" className={field} value={min} placeholder={facets.minPrice != null ? String(Math.floor(facets.minPrice)) : ""} onChange={(e) => setMin(e.target.value)} onBlur={() => min !== filters.minPrice && set({ minPrice: min.trim(), maxPrice: max.trim() })} />
          </label>
          <label className="grid gap-1.5 text-sm text-soft">
            {t.listing.priceMax}
            <input inputMode="decimal" className={field} value={max} placeholder={facets.maxPrice != null ? String(Math.ceil(facets.maxPrice)) : ""} onChange={(e) => setMax(e.target.value)} onBlur={() => max !== filters.maxPrice && set({ minPrice: min.trim(), maxPrice: max.trim() })} />
          </label>
        </div>
      </Group>

      {hasFilters(filters) ? (
        <button type="button" onClick={() => go({ ...filters, brand: [], gender: "", mood: "", family: "", note: "", minPrice: "", maxPrice: "", inStock: false })} className="min-h-11 justify-self-start text-sm text-gold-light underline underline-offset-4">
          {t.listing.clear}
        </button>
      ) : null}
      <button type="submit" className="sr-only">
        {t.search.submit}
      </button>
    </form>
  );
}

/** Phones: "Filtro" opens the same form in a drawer. */
export function MobileFilters({ filters, facets, total }: { filters: ListingFilters; facets: FacetLists; total: number }) {
  const [open, setOpen] = useState(false);
  return (
    <div className="lg:hidden">
      <button type="button" onClick={() => setOpen(true)} aria-expanded={open} className="flex min-h-12 items-center gap-2 rounded-full border border-line-strong px-5 text-text hover:border-gold">
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
          <path d="M4 6h16M7 12h10M10 18h4" />
        </svg>
        {t.listing.openFilters}
      </button>
      <Drawer
        open={open}
        onClose={() => setOpen(false)}
        title={t.listing.filters}
        side="left"
        closeLabel={t.listing.closeFilters}
        footer={
          <button type="button" onClick={() => setOpen(false)} className="min-h-12 w-full rounded-full bg-gold font-semibold text-ground">
            {t.listing.showResults(total)}
          </button>
        }
      >
        <FilterForm filters={filters} facets={facets} idPrefix="m" />
      </Drawer>
    </div>
  );
}

export function SortSelect({ filters: fromUrl }: { filters: ListingFilters }) {
  const { filters, go } = useNavigateFilters(fromUrl);
  const sorts = filters.q ? ["relevance", "newest", "price_asc", "price_desc", "name"] : ["newest", "price_asc", "price_desc", "name"];
  return (
    <label className="flex items-center gap-3 text-sm text-soft">
      {t.listing.sort}
      <select className="min-h-12 rounded-full border border-field bg-ground px-4 text-[15px] text-text" value={filters.sort || (filters.q ? "relevance" : "newest")} onChange={(e) => go({ ...filters, sort: e.target.value === "relevance" ? "" : e.target.value })}>
        {sorts.map((s) => (
          <option key={s} value={s}>
            {t.listing.sorts[s]}
          </option>
        ))}
      </select>
    </label>
  );
}
