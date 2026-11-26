import { Badge, Checkbox, DataTable, Select, StatusBadge } from "@perfumeshop/ui";
import { useNavigate } from "react-router";
import { PageHeader } from "@/components/PageHeader";
import { SearchInput } from "@/components/SearchInput";
import { useCustomers, type CustomerListItem } from "@/features/customers/api";
import { formatDateTime, t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { useSearchState } from "@/lib/use-search-state";

const defaults = { search: "", status: "", refusals: "", page: "1" };
const pageSize = 50;

export const customerBadges = { Active: { label: t.customers.active, tone: "success" as const }, Blocked: { label: t.customers.blocked, tone: "danger" as const } };

export function CustomersPage() {
  const navigate = useNavigate();
  const [state, set] = useSearchState(defaults);
  const page = Number(state.page) || 1;
  const customers = useCustomers({ search: state.search, status: state.status, hasRefusals: state.refusals === "1", page, pageSize });

  return (
    <>
      <PageHeader title={t.customers.title} />
      <DataTable<CustomerListItem>
        caption={t.customers.title}
        toolbar={
          <>
            <SearchInput value={state.search} onChange={(search) => set({ search })} placeholder={t.customers.search} />
            <Select className="w-full sm:w-44" aria-label={t.customers.status} value={state.status} onChange={(e) => set({ status: e.target.value })}>
              <option value="">{t.customers.allStatuses}</option>
              <option value="Active">{t.customers.active}</option>
              <option value="Blocked">{t.customers.blocked}</option>
            </Select>
            <label className="flex items-center gap-2 text-sm">
              <Checkbox checked={state.refusals === "1"} onChange={(e) => set({ refusals: e.target.checked ? "1" : "" })} /> {t.customers.refusalsOnly}
            </label>
          </>
        }
        columns={[
          { key: "phone", header: t.customers.phone, cell: (c) => <span className="font-medium">{c.phone}</span> },
          { key: "name", header: t.customers.name, cell: (c) => c.fullName ?? t.customers.unknownName },
          { key: "orders", header: t.customers.orders, cell: (c) => c.orderCount, className: "text-right tabular-nums" },
          {
            key: "refused",
            header: t.customers.refused,
            cell: (c) => (c.refusedCount > 0 ? <Badge tone="danger">{c.refusedCount}</Badge> : <span className="tabular-nums">0</span>),
            className: "text-right",
          },
          { key: "status", header: t.customers.status, cell: (c) => <StatusBadge status={c.status} map={customerBadges} /> },
          { key: "login", header: t.customers.lastLogin, cell: (c) => formatDateTime(c.lastLoginAtUtc), hideOnMobile: true },
        ]}
        rows={customers.data?.items}
        rowKey={(c) => c.id}
        total={customers.data?.totalCount ?? 0}
        page={page}
        pageSize={pageSize}
        onPageChange={(next) => set({ page: String(next) })}
        isLoading={customers.isLoading}
        error={customers.error ? describeError(customers.error) : null}
        onRetry={() => void customers.refetch()}
        emptyTitle={t.customers.empty}
        onRowClick={(c) => navigate(`/customers/${c.id}`)}
      />
    </>
  );
}
