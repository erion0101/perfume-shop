import { DataTable, formatMoney, Select, StatusBadge, TextField } from "@perfumeshop/ui";
import { useNavigate } from "react-router";
import { PageHeader } from "@/components/PageHeader";
import { SearchInput } from "@/components/SearchInput";
import { usePayments, type Payment } from "@/features/payments/api";
import { paymentBadges } from "@/features/statuses";
import { formatDateTime, t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { useSearchState } from "@/lib/use-search-state";

const defaults = { status: "", from: "", to: "", courier: "", orderNumber: "", page: "1" };
const pageSize = 50;

export function PaymentsPage() {
  const navigate = useNavigate();
  const [state, set] = useSearchState(defaults);
  const page = Number(state.page) || 1;
  const payments = usePayments({ ...state, page, pageSize });

  return (
    <>
      <PageHeader title={t.payments.title} />
      <DataTable<Payment>
        caption={t.payments.title}
        toolbar={
          <>
            <SearchInput value={state.orderNumber} onChange={(orderNumber) => set({ orderNumber })} placeholder={t.payments.orderFilter} className="sm:max-w-44" />
            <SearchInput value={state.courier} onChange={(courier) => set({ courier })} placeholder={t.payments.courierFilter} className="sm:max-w-44" />
            <Select className="w-full sm:w-48" aria-label={t.payments.status} value={state.status} onChange={(e) => set({ status: e.target.value })}>
              <option value="">{t.payments.allStatuses}</option>
              {Object.entries(t.enums.paymentStatus).map(([value, text]) => (
                <option key={value} value={value}>
                  {text}
                </option>
              ))}
            </Select>
            <TextField label={t.payments.from} type="date" value={state.from} onChange={(e) => set({ from: e.target.value })} className="w-full sm:w-40" />
            <TextField label={t.payments.to} type="date" value={state.to} onChange={(e) => set({ to: e.target.value })} className="w-full sm:w-40" />
          </>
        }
        columns={[
          { key: "order", header: t.payments.order, cell: (p) => <span className="font-medium">{p.orderNumber}</span> },
          { key: "status", header: t.payments.status, cell: (p) => <StatusBadge status={p.status} map={paymentBadges} /> },
          { key: "expected", header: t.payments.expected, cell: (p) => formatMoney(p.expectedAmount), className: "text-right tabular-nums" },
          { key: "collected", header: t.payments.collected, cell: (p) => formatMoney(p.collectedAmount), className: "text-right tabular-nums" },
          { key: "fee", header: t.payments.fee, cell: (p) => formatMoney(p.courierFee), className: "text-right tabular-nums", hideOnMobile: true },
          { key: "net", header: t.payments.net, cell: (p) => formatMoney(p.netReceived), className: "text-right tabular-nums" },
          { key: "courier", header: t.payments.courier, cell: (p) => p.courierName ?? t.common.none, hideOnMobile: true },
          { key: "placed", header: t.payments.placed, cell: (p) => formatDateTime(p.placedAtUtc), hideOnMobile: true },
        ]}
        rows={payments.data?.items}
        rowKey={(p) => p.id}
        total={payments.data?.totalCount ?? 0}
        page={page}
        pageSize={pageSize}
        onPageChange={(next) => set({ page: String(next) })}
        isLoading={payments.isLoading}
        error={payments.error ? describeError(payments.error) : null}
        onRetry={() => void payments.refetch()}
        emptyTitle={t.payments.empty}
        onRowClick={(p) => navigate(`/orders/${p.orderId}`)}
      />
    </>
  );
}
