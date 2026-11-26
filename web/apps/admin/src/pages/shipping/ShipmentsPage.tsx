import { DataTable, formatMoney, Select, StatusBadge } from "@perfumeshop/ui";
import { useNavigate } from "react-router";
import { PageHeader } from "@/components/PageHeader";
import { SearchInput } from "@/components/SearchInput";
import { useCouriers, useShipments, type Shipment } from "@/features/shipping/api";
import { shipmentBadges } from "@/features/statuses";
import { formatDateTime, t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { useSearchState } from "@/lib/use-search-state";

const defaults = { status: "", courierId: "", tracking: "", orderNumber: "", page: "1" };
const pageSize = 50;

export function ShipmentsPage() {
  const navigate = useNavigate();
  const [state, set] = useSearchState(defaults);
  const page = Number(state.page) || 1;
  const shipments = useShipments({ ...state, page, pageSize });
  const couriers = useCouriers();

  return (
    <>
      <PageHeader title={t.shipments.title} />
      <DataTable<Shipment>
        caption={t.shipments.title}
        toolbar={
          <>
            <SearchInput value={state.tracking} onChange={(tracking) => set({ tracking })} placeholder={t.shipments.trackingFilter} className="sm:max-w-48" />
            <SearchInput value={state.orderNumber} onChange={(orderNumber) => set({ orderNumber })} placeholder={t.orders.numberFilter} className="sm:max-w-44" />
            <Select className="w-full sm:w-48" aria-label={t.shipments.courier} value={state.courierId} onChange={(e) => set({ courierId: e.target.value })}>
              <option value="">{t.shipments.allCouriers}</option>
              {(couriers.data ?? []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </Select>
            <Select className="w-full sm:w-48" aria-label={t.shipments.status} value={state.status} onChange={(e) => set({ status: e.target.value })}>
              <option value="">{t.shipments.allStatuses}</option>
              {Object.entries(t.enums.shipmentStatus).map(([value, text]) => (
                <option key={value} value={value}>
                  {text}
                </option>
              ))}
            </Select>
          </>
        }
        columns={[
          { key: "tracking", header: t.shipments.tracking, cell: (s) => <span className="font-medium">{s.trackingNumber}</span> },
          { key: "order", header: t.shipments.number, cell: (s) => s.orderNumber },
          { key: "status", header: t.shipments.status, cell: (s) => <StatusBadge status={s.status} map={shipmentBadges} /> },
          { key: "courier", header: t.shipments.courier, cell: (s) => s.courierName, hideOnMobile: true },
          { key: "recipient", header: t.shipments.recipient, cell: (s) => `${s.recipient.fullName}, ${s.recipient.city}`, hideOnMobile: true },
          { key: "cod", header: t.shipments.cod, cell: (s) => formatMoney(s.codAmount), className: "text-right tabular-nums" },
          { key: "created", header: t.shipments.created, cell: (s) => formatDateTime(s.createdAtUtc), hideOnMobile: true },
        ]}
        rows={shipments.data?.items}
        rowKey={(s) => s.id}
        total={shipments.data?.totalCount ?? 0}
        page={page}
        pageSize={pageSize}
        onPageChange={(next) => set({ page: String(next) })}
        isLoading={shipments.isLoading}
        error={shipments.error ? describeError(shipments.error) : null}
        onRetry={() => void shipments.refetch()}
        emptyTitle={t.shipments.empty}
        onRowClick={(s) => navigate(`/shipping/shipments/${s.id}`)}
      />
    </>
  );
}
