import Link from "next/link";
import { Pagination } from "@/components/Pagination";
import { ProductGrid } from "@/components/product/ProductCard";
import { capitalize, label, noteName, t } from "@/i18n";
import { getFacets, listFragrances, orEmpty } from "@/lib/catalog";
import { apiBrand, hasFilters, PAGE_SIZE, toSearch, type ListingFilters } from "@/lib/listing-params";
import { FilterForm, MobileFilters, SortSelect, type FacetLists } from "./Filters";

/**
 * Results + filters for /parfumet, /markat/[slug] (brand fixed) and /kerko (query fixed). Filters live in the URL.
 */
export async function Listing({ filters, basePath, fixedBrand, showBrandFilter = true }: { filters: ListingFilters; basePath: string; fixedBrand?: string; showBrandFilter?: boolean }) {
  const brand = fixedBrand ?? apiBrand(filters);
  const [page, facets] = await Promise.all([
    listFragrances({
      brand,
      gender: filters.gender || undefined,
      family: filters.family || undefined,
      mood: filters.mood || undefined,
      note: filters.note || undefined,
      minPrice: filters.minPrice ? Number(filters.minPrice) : undefined,
      maxPrice: filters.maxPrice ? Number(filters.maxPrice) : undefined,
      inStock: filters.inStock,
      q: filters.q || undefined,
      sort: filters.sort || undefined,
      page: filters.page,
      pageSize: PAGE_SIZE,
    }),
    orEmpty(getFacets(), null),
  ]);

  const facetLists: FacetLists = {
    brands: showBrandFilter ? (facets?.brands ?? []) : [],
    moods: facets?.moods ?? [],
    families: facets?.families ?? [],
    notes: facets?.notes ?? [],
    minPrice: facets?.minPrice ?? null,
    maxPrice: facets?.maxPrice ?? null,
  };
  const totalPages = Math.max(1, Math.ceil(page.totalCount / PAGE_SIZE));
  const href = (p: number) => {
    const query = toSearch({ ...filters, brand: fixedBrand ? [] : filters.brand, page: p });
    return query ? `${basePath}?${query}` : basePath;
  };

  const chips = [
    ...(fixedBrand ? [] : filters.brand.map((b) => ({ key: `b-${b}`, text: facets?.brands.find((f) => f.value === b)?.label ?? b, without: { brand: filters.brand.filter((x) => x !== b) } }))),
    filters.mood && { key: "mood", text: label(t.enums.mood, filters.mood), without: { mood: "" } },
    filters.gender && { key: "gender", text: label(t.enums.gender, filters.gender), without: { gender: "" } },
    filters.family && { key: "family", text: label(t.enums.family, filters.family), without: { family: "" } },
    filters.note && { key: "note", text: capitalize(noteName(filters.note)), without: { note: "" } },
    (filters.minPrice || filters.maxPrice) && { key: "price", text: `${filters.minPrice || "0"}–${filters.maxPrice || "…"} €`, without: { minPrice: "", maxPrice: "" } },
    filters.inStock && { key: "stock", text: t.listing.inStock, without: { inStock: false } },
  ].filter(Boolean) as { key: string; text: string; without: Partial<ListingFilters> }[];

  return (
    <div className="mx-auto grid max-w-[1440px] gap-10 px-5 pt-8 pb-20 md:px-14 lg:grid-cols-[260px_1fr]">
      <aside aria-label={t.listing.filters} className="hidden lg:block">
        <h2 className="m-0 mb-6 font-sans text-sm font-semibold uppercase tracking-[0.12em] text-soft">{t.listing.filters}</h2>
        <FilterForm filters={filters} facets={facetLists} idPrefix="d" />
      </aside>
      <div className="min-w-0">
        <div className="mb-6 flex flex-wrap items-center justify-between gap-4">
          <div className="flex items-center gap-4">
            <MobileFilters filters={filters} facets={facetLists} total={page.totalCount} />
            <p className="m-0 text-sm text-subtle" aria-live="polite">
              {t.listing.results(page.totalCount)}
            </p>
          </div>
          <SortSelect filters={filters} />
        </div>
        {chips.length > 0 ? (
          <ul aria-label={t.listing.activeFilters} className="m-0 mb-6 flex list-none flex-wrap gap-2 p-0">
            {chips.map((c) => {
              const query = toSearch({ ...filters, ...c.without, brand: fixedBrand ? [] : (c.without.brand ?? filters.brand), page: 1 });
              return (
                <li key={c.key}>
                  <Link href={query ? `${basePath}?${query}` : basePath} aria-label={t.listing.removeFilter(c.text)} className="inline-flex min-h-11 items-center gap-2 rounded-full border border-gold px-4 text-sm text-gold-light">
                    {c.text} <span aria-hidden="true">×</span>
                  </Link>
                </li>
              );
            })}
          </ul>
        ) : null}
        {page.items.length === 0 ? (
          <div className="rounded-[20px] border border-line bg-panel px-6 py-16 text-center">
            <p className="m-0 font-display text-2xl text-heading">{t.listing.empty}</p>
            <p className="m-0 mt-2 text-muted">{t.listing.emptyHint}</p>
            {hasFilters(filters) ? (
              <Link href={basePath} className="mt-6 inline-flex min-h-11 items-center rounded-full border border-line-strong px-6 text-text hover:border-gold">
                {t.listing.clear}
              </Link>
            ) : null}
          </div>
        ) : (
          <ProductGrid items={page.items} priorityCount={filters.page === 1 ? 2 : 0} />
        )}
        <Pagination page={filters.page} totalPages={totalPages} href={href} />
      </div>
    </div>
  );
}
