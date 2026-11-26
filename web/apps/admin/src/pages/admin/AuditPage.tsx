import { DataTable, Select, TextField } from "@perfumeshop/ui";
import { useState } from "react";
import { PageHeader } from "@/components/PageHeader";
import { SearchInput } from "@/components/SearchInput";
import { useAudit, type AuditEntry } from "@/features/admin/api";
import { formatDateTime, t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { useSearchState } from "@/lib/use-search-state";

const defaults = { action: "", actor: "", search: "", from: "", to: "", page: "1" };
const pageSize = 50;

function Changes({ json }: { json: string | null }) {
  const [open, setOpen] = useState(false);
  if (!json) {
    return null;
  }

  return (
    <span className="grid gap-1">
      <button type="button" className="text-left text-xs text-primary underline-offset-4 hover:underline" aria-expanded={open} onClick={() => setOpen(!open)}>
        {t.audit.details}
      </button>
      {open ? <pre className="max-w-md overflow-x-auto whitespace-pre-wrap rounded-md bg-muted p-2 text-xs">{JSON.stringify(JSON.parse(json), null, 2)}</pre> : null}
    </span>
  );
}

export function AuditPage() {
  const [state, set] = useSearchState(defaults);
  const page = Number(state.page) || 1;
  const audit = useAudit({ ...state, page, pageSize });

  return (
    <>
      <PageHeader title={t.audit.title} description={t.audit.intro} />
      <DataTable<AuditEntry>
        caption={t.audit.title}
        toolbar={
          <>
            <Select className="w-full sm:w-44" aria-label={t.audit.area} value={state.action} onChange={(e) => set({ action: e.target.value })}>
              <option value="">{t.audit.allAreas}</option>
              {Object.entries(t.audit.areas).map(([prefix, name]) => (
                <option key={prefix} value={prefix}>
                  {name}
                </option>
              ))}
            </Select>
            <SearchInput value={state.actor} onChange={(actor) => set({ actor })} placeholder={t.audit.actor} className="sm:max-w-52" />
            <SearchInput value={state.search} onChange={(search) => set({ search })} placeholder={t.audit.search} />
            <TextField label={t.orders.from} type="date" value={state.from} onChange={(e) => set({ from: e.target.value })} className="w-full sm:w-40" />
            <TextField label={t.orders.to} type="date" value={state.to} onChange={(e) => set({ to: e.target.value })} className="w-full sm:w-40" />
          </>
        }
        columns={[
          { key: "when", header: t.audit.when, cell: (a) => <span className="whitespace-nowrap tabular-nums">{formatDateTime(a.occurredAtUtc)}</span> },
          { key: "who", header: t.audit.who, cell: (a) => a.actorEmail ?? (a.actorId === "system" ? t.audit.system : a.actorId.slice(0, 8)) },
          { key: "action", header: t.audit.action, cell: (a) => <code className="text-xs">{a.action}</code>, hideOnMobile: true },
          {
            key: "summary",
            header: t.audit.summary,
            cell: (a) => (
              <span className="grid gap-1">
                <span>{a.summary}</span>
                <Changes json={a.changes} />
              </span>
            ),
          },
        ]}
        rows={audit.data?.items}
        rowKey={(a) => a.id}
        total={audit.data?.totalCount ?? 0}
        page={page}
        pageSize={pageSize}
        onPageChange={(next) => set({ page: String(next) })}
        isLoading={audit.isLoading}
        error={audit.error ? describeError(audit.error) : null}
        onRetry={() => void audit.refetch()}
        emptyTitle={t.audit.empty}
      />
    </>
  );
}
