import { Badge, Button, Card, CardContent, EmptyState, ErrorState, formatMoney, LoadingState, TextField } from "@perfumeshop/ui";
import { Link } from "react-router";
import { PageHeader } from "@/components/PageHeader";
import { useDailyCash } from "@/features/payments/api";
import { t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { useSearchState } from "@/lib/use-search-state";

const defaults = { date: "" };
const th = "px-2 py-2 text-right font-medium text-muted-foreground";
const td = "px-2 py-2 text-right tabular-nums";

export function DailyCashPage() {
  const [state, set] = useSearchState(defaults);
  const daily = useDailyCash(state.date);
  const d = daily.data;

  return (
    <>
      <PageHeader
        title={t.daily.title}
        actions={<TextField label={t.daily.date} type="date" value={state.date || d?.date || ""} onChange={(e) => set({ date: e.target.value })} className="w-44" />}
      />
      {daily.isLoading ? (
        <LoadingState />
      ) : daily.error || !d ? (
        <ErrorState message={describeError(daily.error)} onRetry={() => void daily.refetch()} />
      ) : (
        <div className="grid gap-4">
          {d.failedReceipts > 0 ? (
            <p role="alert" className="flex flex-wrap items-center gap-2 rounded-md border border-red-200 bg-red-50 p-3 text-sm">
              <Badge tone="danger">{t.daily.failedReceipts(d.failedReceipts)}</Badge>
              <Button asChild size="sm" variant="outline">
                <Link to="/payments/receipts?status=Failed">{t.handover.retryReceipts}</Link>
              </Button>
            </p>
          ) : null}
          <Card>
            <CardContent className="overflow-x-auto">
              {d.couriers.length === 0 ? (
                <EmptyState title={t.daily.empty} />
              ) : (
                <table className="w-full min-w-[44rem] text-sm" aria-label={t.daily.title}>
                  <thead>
                    <tr className="border-b border-border">
                      <th className="px-2 py-2 text-left font-medium text-muted-foreground">{t.daily.courier}</th>
                      <th className={th}>{t.daily.shipped}</th>
                      <th className={th}>{t.daily.expected}</th>
                      <th className={th}>{t.daily.collectedCount}</th>
                      <th className={th}>{t.daily.collected}</th>
                      <th className={th}>{t.daily.fees}</th>
                      <th className={th}>{t.daily.net}</th>
                      <th className={th}>{t.daily.outstanding}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {[...d.couriers, { ...d.total, courier: t.daily.total }].map((c, index) => (
                      <tr key={`${c.courier}-${index}`} className={index === d.couriers.length ? "border-t-2 border-border font-semibold" : "border-b border-border"}>
                        <td className="px-2 py-2">{c.courier}</td>
                        <td className={td}>{c.shippedCount}</td>
                        <td className={td}>{formatMoney(c.expected)}</td>
                        <td className={td}>{c.collectedCount}</td>
                        <td className={td}>{formatMoney(c.collected)}</td>
                        <td className={td}>{formatMoney(c.fees)}</td>
                        <td className={td}>{formatMoney(c.net)}</td>
                        <td className={td}>{formatMoney(c.outstandingAtCourier)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </CardContent>
          </Card>
        </div>
      )}
    </>
  );
}
