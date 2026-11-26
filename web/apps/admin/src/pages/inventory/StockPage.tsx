import { Button, Checkbox, DataTable, Input, StatusBadge, toast, type SortState } from "@perfumeshop/ui";
import { PackagePlus } from "lucide-react";
import { useState } from "react";
import { Link, useNavigate } from "react-router";
import { PageHeader } from "@/components/PageHeader";
import { useSetThreshold, useStock, type StockItem } from "@/features/inventory/api";
import { formatDateTime, t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { useSearchState } from "@/lib/use-search-state";

const defaults = { low: "", out: "", sort: "available", page: "1" };
const pageSize = 50;

export const stockBadges = {
  ok: { label: t.stock.ok, tone: "success" as const },
  low: { label: t.stock.low, tone: "warning" as const },
  out: { label: t.stock.out, tone: "danger" as const },
};

export const stockState = (s: StockItem) => (s.available <= 0 ? "out" : s.isLowStock ? "low" : "ok");

export const variantName = (s: StockItem) => (s.variant ? `${s.variant.brandName} ${s.variant.fragranceName}${s.variant.size ? ` ${s.variant.size}` : ""}` : t.stock.unknownVariant);

/** Inline threshold editor: saves on blur / Enter. */
function ThresholdInput({ item }: { item: StockItem }) {
  const setThreshold = useSetThreshold();
  const [text, setText] = useState(String(item.lowStockThreshold));

  async function save() {
    const value = Number(text);
    if (text === String(item.lowStockThreshold)) {
      return;
    }

    if (!/^\d+$/.test(text) || value > 1000) {
      toast.error(t.stock.thresholdInvalid);
      setText(String(item.lowStockThreshold));
      return;
    }

    try {
      await setThreshold.mutateAsync({ variantId: item.variantId, lowStockThreshold: value });
      toast.success(t.stock.thresholdSaved);
    } catch (error) {
      toast.error(describeError(error));
      setText(String(item.lowStockThreshold));
    }
  }

  return (
    <Input
      className="h-8 w-16 text-right"
      inputMode="numeric"
      aria-label={`${t.stock.threshold} ${item.variant?.sku ?? ""}`}
      value={text}
      disabled={setThreshold.isPending}
      onClick={(e) => e.stopPropagation()}
      onChange={(e) => setText(e.target.value)}
      onBlur={() => void save()}
      onKeyDown={(e) => {
        if (e.key === "Enter") {
          e.currentTarget.blur();
        }
      }}
    />
  );
}

export function StockPage() {
  const navigate = useNavigate();
  const [state, set] = useSearchState(defaults);
  const page = Number(state.page) || 1;
  const stock = useStock({ lowStock: state.low === "1", outOfStock: state.out === "1", sort: state.sort, page, pageSize });
  const sort: SortState = state.sort.startsWith("-") ? { key: state.sort.slice(1), direction: "desc" } : { key: state.sort, direction: "asc" };

  return (
    <>
      <PageHeader
        title={t.stock.title}
        description={t.stock.thresholdHint}
        actions={
          <Button asChild>
            <Link to="/inventory/receive">
              <PackagePlus /> {t.stock.receive}
            </Link>
          </Button>
        }
      />
      <DataTable<StockItem>
        caption={t.stock.title}
        toolbar={
          <>
            <label className="flex items-center gap-2 text-sm">
              <Checkbox checked={state.low === "1"} onChange={(e) => set({ low: e.target.checked ? "1" : "" })} />
              {t.stock.lowStockOnly}
            </label>
            <label className="flex items-center gap-2 text-sm">
              <Checkbox checked={state.out === "1"} onChange={(e) => set({ out: e.target.checked ? "1" : "" })} />
              {t.stock.outOfStockOnly}
            </label>
          </>
        }
        columns={[
          {
            key: "product",
            header: t.stock.product,
            cell: (s) => (
              <span className="grid">
                <span className="font-medium">{variantName(s)}</span>
                <span className="text-xs text-muted-foreground">{s.variant?.sku}</span>
              </span>
            ),
          },
          { key: "onHand", header: t.stock.onHand, sortKey: "onHand", cell: (s) => s.onHand, className: "text-right tabular-nums" },
          { key: "reserved", header: t.stock.reserved, cell: (s) => s.reserved, className: "text-right tabular-nums", hideOnMobile: true },
          { key: "available", header: t.stock.available, sortKey: "available", cell: (s) => <span className="font-semibold">{s.available}</span>, className: "text-right tabular-nums" },
          { key: "threshold", header: t.stock.threshold, cell: (s) => <ThresholdInput item={s} />, className: "text-right" },
          { key: "state", header: t.stock.title, cell: (s) => <StatusBadge status={stockState(s)} map={stockBadges} /> },
          { key: "updated", header: t.stock.updated, sortKey: "updated", cell: (s) => formatDateTime(s.updatedAtUtc), hideOnMobile: true },
        ]}
        rows={stock.data?.items}
        rowKey={(s) => s.variantId}
        total={stock.data?.totalCount ?? 0}
        page={page}
        pageSize={pageSize}
        onPageChange={(next) => set({ page: String(next) })}
        sort={sort}
        onSortChange={(next) => set({ sort: next.direction === "desc" ? `-${next.key}` : next.key })}
        isLoading={stock.isFetching}
        error={stock.error ? describeError(stock.error) : null}
        onRetry={() => void stock.refetch()}
        emptyTitle={t.stock.empty}
        onRowClick={(s) => navigate(`/inventory/items/${s.variantId}`)}
      />
    </>
  );
}
