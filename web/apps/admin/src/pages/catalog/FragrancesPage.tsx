import { Button, DataTable, Select, StatusBadge, type SortState } from "@perfumeshop/ui";
import { Download } from "lucide-react";
import { Link, useNavigate } from "react-router";
import { PageHeader } from "@/components/PageHeader";
import { SearchInput } from "@/components/SearchInput";
import { useBrands, useFragrances, type FragranceListItem } from "@/features/catalog/api";
import { formatDate, label, t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { useSearchState } from "@/lib/use-search-state";

const defaults = { search: "", brandId: "", status: "", inStock: "", sort: "-updated", page: "1" };
const pageSize = 25;

export const fragranceStatusBadges = {
  Draft: { label: t.enums.fragranceStatus.Draft!, tone: "neutral" as const },
  Published: { label: t.enums.fragranceStatus.Published!, tone: "success" as const },
};

function toSort(value: string): SortState {
  return value.startsWith("-") ? { key: value.slice(1), direction: "desc" } : { key: value, direction: "asc" };
}

export function FragrancesPage() {
  const navigate = useNavigate();
  const [state, set] = useSearchState(defaults);
  const page = Number(state.page) || 1;
  const fragrances = useFragrances({ ...state, page, pageSize });
  const brands = useBrands();

  return (
    <>
      <PageHeader
        title={t.fragrances.title}
        actions={
          <Button asChild>
            <Link to="/catalog/import">
              <Download /> {t.fragrances.importNew}
            </Link>
          </Button>
        }
      />
      <DataTable<FragranceListItem>
        caption={t.fragrances.title}
        toolbar={
          <>
            <SearchInput value={state.search} onChange={(search) => set({ search })} />
            <Select className="w-full sm:w-48" aria-label={t.fragrances.brand} value={state.brandId} onChange={(e) => set({ brandId: e.target.value })}>
              <option value="">{t.fragrances.allBrands}</option>
              {(brands.data ?? []).map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </Select>
            <Select className="w-full sm:w-44" aria-label={t.fragrances.status} value={state.status} onChange={(e) => set({ status: e.target.value })}>
              <option value="">{t.fragrances.allStatuses}</option>
              <option value="Draft">{t.enums.fragranceStatus.Draft}</option>
              <option value="Published">{t.enums.fragranceStatus.Published}</option>
            </Select>
            <Select className="w-full sm:w-40" aria-label={t.fragrances.stock} value={state.inStock} onChange={(e) => set({ inStock: e.target.value })}>
              <option value="">{t.fragrances.anyStock}</option>
              <option value="true">{t.fragrances.inStock}</option>
              <option value="false">{t.fragrances.outOfStock}</option>
            </Select>
          </>
        }
        columns={[
          { key: "name", header: t.fragrances.name, sortKey: "name", cell: (f) => <span className="font-medium">{f.name}</span> },
          { key: "brand", header: t.fragrances.brand, sortKey: "brand", cell: (f) => f.brandName },
          { key: "concentration", header: t.fragrances.concentration, cell: (f) => label(t.enums.concentration, f.concentration), hideOnMobile: true },
          { key: "status", header: t.fragrances.status, cell: (f) => <StatusBadge status={f.status as "Draft"} map={fragranceStatusBadges} /> },
          { key: "variants", header: t.fragrances.variants, cell: (f) => `${f.activeVariantCount}/${f.variantCount}`, className: "text-right", hideOnMobile: true },
          {
            key: "stock",
            header: t.fragrances.stock,
            cell: (f) => <StatusBadge status={f.inStock ? "in" : "out"} map={{ in: { label: t.fragrances.inStock, tone: "success" }, out: { label: t.fragrances.outOfStock, tone: "warning" } }} />,
          },
          { key: "updated", header: t.fragrances.updated, sortKey: "updated", cell: (f) => formatDate(f.updatedAtUtc), hideOnMobile: true },
        ]}
        rows={fragrances.data?.items}
        rowKey={(f) => f.id}
        total={fragrances.data?.totalCount ?? 0}
        page={page}
        pageSize={pageSize}
        onPageChange={(next) => set({ page: String(next) })}
        sort={toSort(state.sort)}
        onSortChange={(sort) => set({ sort: sort.direction === "desc" ? `-${sort.key}` : sort.key })}
        isLoading={fragrances.isFetching}
        error={fragrances.error ? describeError(fragrances.error) : null}
        onRetry={() => void fragrances.refetch()}
        emptyTitle={t.fragrances.empty}
        emptyText={t.fragrances.emptyHint}
        onRowClick={(f) => navigate(`/catalog/fragrances/${f.id}`)}
      />
    </>
  );
}
