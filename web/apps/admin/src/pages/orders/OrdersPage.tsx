import { Badge, cn, DataTable, formatMoney, StatusBadge, TextField, type SortState } from "@perfumeshop/ui";
import { RefreshCw } from "lucide-react";
import { useState } from "react";
import { useNavigate } from "react-router";
import { PageHeader } from "@/components/PageHeader";
import { SearchInput } from "@/components/SearchInput";
import { useOrderCounts, useOrders, type OrderListItem } from "@/features/orders/api";
import { orderBadges } from "@/features/statuses";
import { formatDateTime, t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { useSearchState } from "@/lib/use-search-state";

const defaults = { status: "", from: "", to: "", phone: "", number: "", courier: "", sort: "-placed", page: "1" };
const pageSize = 50;
const tabs = ["", "Pending", "Confirmed", "Shipped", "Delivered", "CashCollected", "Refused", "Cancelled", "Expired"] as const;

export function RiskBadge({ refused }: { refused: number }) {
  return refused > 0 ? <Badge tone="danger">{t.orders.risk(refused)}</Badge> : null;
}

export function OrdersPage() {
  const navigate = useNavigate();
  const [state, set] = useSearchState(defaults);
  const [openedAt] = useState(() => Date.now());
  const page = Number(state.page) || 1;
  const orders = useOrders({ ...state, page, pageSize });
  const counts = useOrderCounts(state);
  const sort: SortState = { key: "placed", direction: state.sort === "placed" ? "asc" : "desc" };

  return (
    <>
      <PageHeader
        title={t.orders.title}
        description={
          <span className="inline-flex items-center gap-1.5">
            <RefreshCw className={cn("size-3.5", orders.isFetching && "animate-spin")} aria-hidden />
            {t.orders.autoRefresh}
            {orders.dataUpdatedAt ? ` · ${t.orders.lastUpdated(new Date(orders.dataUpdatedAt).toLocaleTimeString("sq-XK", { hour: "2-digit", minute: "2-digit", second: "2-digit" }))}` : null}
          </span>
        }
      />
      <div role="tablist" aria-label={t.orders.status} className="mb-3 flex gap-1 overflow-x-auto pb-1">
        {tabs.map((status) => {
          const count = status ? counts.data?.counts[status] : counts.data?.total;
          const active = state.status === status;
          return (
            <button
              key={status || "all"}
              type="button"
              role="tab"
              aria-selected={active}
              onClick={() => set({ status })}
              className={cn(
                "flex shrink-0 items-center gap-1.5 rounded-full border px-3 py-1.5 text-sm",
                active ? "border-primary bg-primary text-primary-foreground" : "border-border bg-background hover:bg-muted",
              )}
            >
              {status ? t.enums.orderStatus[status] : t.orders.allStatuses}
              <span className={cn("rounded-full px-1.5 text-xs tabular-nums", active ? "bg-primary-foreground/20" : "bg-muted")}>{count ?? "…"}</span>
            </button>
          );
        })}
      </div>
      <DataTable<OrderListItem>
        caption={t.orders.title}
        toolbar={
          <>
            <SearchInput value={state.number} onChange={(number) => set({ number })} placeholder={t.orders.numberFilter} className="sm:max-w-44" />
            <SearchInput value={state.phone} onChange={(phone) => set({ phone })} placeholder={t.orders.phoneFilter} className="sm:max-w-48" />
            <SearchInput value={state.courier} onChange={(courier) => set({ courier })} placeholder={t.orders.courierFilter} className="sm:max-w-44" />
            <TextField label={t.orders.from} type="date" value={state.from} onChange={(e) => set({ from: e.target.value })} className="w-full sm:w-40" />
            <TextField label={t.orders.to} type="date" value={state.to} onChange={(e) => set({ to: e.target.value })} className="w-full sm:w-40" />
          </>
        }
        columns={[
          {
            key: "number",
            header: t.orders.number,
            cell: (o) => (
              <span className="inline-flex items-center gap-1.5 font-medium">
                {o.number}
                {Date.parse(o.placedAtUtc) > openedAt ? <Badge tone="warning">{t.orders.isNew}</Badge> : null}
              </span>
            ),
          },
          { key: "status", header: t.orders.status, cell: (o) => <StatusBadge status={o.status} map={orderBadges} /> },
          {
            key: "customer",
            header: t.orders.customer,
            cell: (o) => (
              <span className="grid">
                <span>{o.customerName}</span>
                <span className="text-xs text-muted-foreground">
                  {o.customerPhone} · {o.city}
                </span>
                <RiskBadge refused={o.customerRefusedCount} />
              </span>
            ),
          },
          { key: "total", header: t.orders.total, cell: (o) => formatMoney(o.total), className: "text-right tabular-nums" },
          { key: "courier", header: t.orders.courier, cell: (o) => (o.courierName ? `${o.courierName} · ${o.trackingNumber ?? ""}` : t.common.none), hideOnMobile: true },
          { key: "placed", header: t.orders.placed, sortKey: "placed", cell: (o) => formatDateTime(o.placedAtUtc) },
        ]}
        rows={orders.data?.items}
        rowKey={(o) => o.id}
        total={orders.data?.totalCount ?? 0}
        page={page}
        pageSize={pageSize}
        onPageChange={(next) => set({ page: String(next) })}
        sort={sort}
        onSortChange={(next) => set({ sort: next.direction === "asc" ? "placed" : "-placed" })}
        isLoading={orders.isLoading}
        error={orders.error ? describeError(orders.error) : null}
        onRetry={() => void orders.refetch()}
        emptyTitle={t.orders.empty}
        onRowClick={(o) => navigate(`/orders/${o.id}`)}
      />
    </>
  );
}
