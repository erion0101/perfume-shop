import { BarList, Card, CardContent, ColumnChart, ErrorState, formatMoney, LoadingState, StatTile, type ChartDatum } from "@perfumeshop/ui";
import { AlertTriangle, CheckCircle2 } from "lucide-react";
import type { ReactNode } from "react";
import { Link, useNavigate } from "react-router";
import { PageHeader } from "@/components/PageHeader";
import { useStock } from "@/features/inventory/api";
import { useMessages } from "@/features/notifications/api";
import { useOrdersDashboard, usePaymentTotals } from "@/features/settings/api";
import { useCodHeldReport, useInTransitReport } from "@/features/shipping/api";
import { formatDate, t } from "@/i18n";
import { useAuth } from "@/lib/auth";
import { describeError } from "@/lib/problem";

/** Parcels at the courier longer than this are an alert (same default as the reports page). */
export const longTransitDays = 3;

/** A headline number that opens the filtered list behind it. */
function LinkedTile({ to, ...tile }: { to: string } & Parameters<typeof StatTile>[0]) {
  return (
    <Link to={to} className="block rounded-lg focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring hover:[&>div]:border-primary">
      <StatTile {...tile} />
    </Link>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="grid gap-2" aria-label={title}>
      <h2 className="text-sm font-semibold text-muted-foreground">{title}</h2>
      {children}
    </section>
  );
}

const num = (v: number | undefined) => (v === undefined ? "…" : v);
const dayLabel = (iso: string) => iso.slice(5).split("-").reverse().join(".");

export function DashboardPage() {
  const { isAdmin } = useAuth();
  const navigate = useNavigate();
  const orders = useOrdersDashboard();
  const totals = usePaymentTotals();
  const held = useCodHeldReport();
  const transit = useInTransitReport(longTransitDays);
  const low = useStock({ lowStock: true, outOfStock: false, sort: "available", page: 1, pageSize: 1 });
  const failedMessages = useMessages({ status: "Failed", channel: "", templateKey: "", recipient: "", orderNumber: "", from: "", to: "", page: 1, pageSize: 1 });

  if (orders.isLoading) {
    return <LoadingState />;
  }

  if (orders.error || !orders.data) {
    return <ErrorState message={describeError(orders.error)} onRetry={() => void orders.refetch()} />;
  }

  const d = orders.data;
  const today = d.today.date;
  const owed = totals.data?.outstandingAtCouriers;
  const overdue = (held.data ?? []).reduce((sum, c) => sum + c.overdueAmount, 0);
  const alerts = [
    { key: "receipts", label: t.home.failedReceipts, value: totals.data?.failedReceipts, to: "/payments/receipts?status=Failed" },
    { key: "stock", label: t.home.lowStock, value: low.data?.totalCount, to: "/inventory?low=1" },
    { key: "transit", label: t.home.longTransit, value: transit.data?.length, to: "/shipping/reports" },
    { key: "messages", label: t.home.failedNotifications, value: failedMessages.data?.totalCount, to: "/notifications/messages?status=Failed" },
  ];
  const active = alerts.filter((a) => (a.value ?? 0) > 0);
  const loaded = alerts.every((a) => a.value !== undefined);

  const days: ChartDatum[] = d.last14Days.map((day) => ({ key: day.date, label: dayLabel(day.date), value: day.orders, detail: t.home.ordersDetail(day.orders, formatMoney(day.revenue)) }));
  const top: ChartDatum[] = d.topSelling30Days.map((s) => ({
    key: s.fragranceId,
    label: `${s.brandName} ${s.fragranceName}`,
    value: s.quantity,
    detail: t.home.topDetail(s.quantity, formatMoney(s.revenue)),
  }));
  const r = d.refusals30Days;

  return (
    <>
      <PageHeader title={t.home.title} description={formatDate(today)} />
      <div className="grid gap-6">
        <Section title={t.home.alerts}>
          {!loaded ? null : active.length === 0 ? (
            <p className="flex items-center gap-2 rounded-lg border border-emerald-200 bg-background p-3 text-sm">
              <CheckCircle2 className="size-4 text-emerald-700" aria-hidden /> {t.home.allGood}
            </p>
          ) : (
            <ul className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
              {active.map((a) => (
                <li key={a.key}>
                  <LinkedTile
                    to={a.to}
                    tone="danger"
                    label={a.label}
                    value={
                      <span className="flex items-center gap-2">
                        <AlertTriangle className="size-5 text-destructive" aria-hidden /> {a.value}
                      </span>
                    }
                  />
                </li>
              ))}
            </ul>
          )}
        </Section>

        <Section title={t.home.today}>
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
            <LinkedTile to={`/orders?from=${today}&to=${today}`} label={t.home.placed} value={d.today.placed} />
            <LinkedTile to="/orders/queue" label={t.home.pending} value={d.pendingNow} tone={d.pendingNow > 0 ? "warning" : "neutral"} />
            <LinkedTile to="/orders?status=Shipped" label={t.home.shipped} value={d.today.shipped} />
            <LinkedTile to="/orders?status=Delivered" label={t.home.delivered} value={d.today.delivered} />
            <LinkedTile to="/orders?status=Refused" label={t.home.refused} value={d.today.refused} tone={d.today.refused > 0 ? "warning" : "neutral"} />
          </div>
        </Section>

        <Section title={t.home.money}>
          <div className="grid gap-2 sm:grid-cols-3">
            <LinkedTile to={`/payments/daily?date=${today}`} label={t.home.collectedToday} value={totals.data ? formatMoney(totals.data.collectedToday) : "…"} />
            <LinkedTile
              to={totals.data ? `/payments?status=Collected&from=${totals.data.weekStart}` : "/payments?status=Collected"}
              label={t.home.collectedWeek}
              value={totals.data ? formatMoney(totals.data.collectedThisWeek) : "…"}
              hint={totals.data ? t.home.since(formatDate(totals.data.weekStart)) : undefined}
            />
            <LinkedTile
              to="/shipping/reports"
              label={t.home.owed}
              value={owed === undefined ? "…" : formatMoney(owed)}
              tone={overdue > 0 ? "danger" : "neutral"}
              hint={overdue > 0 ? <span className="font-semibold text-destructive">{t.home.overdue(formatMoney(overdue))}</span> : undefined}
            />
          </div>
        </Section>

        <div className="grid gap-4 lg:grid-cols-[2fr_1fr]">
          <Card>
            <CardContent className="pt-4">
              <ColumnChart title={t.home.ordersChart} data={days} onSelect={(day) => navigate(`/orders?from=${day.key}&to=${day.key}`)} />
            </CardContent>
          </Card>
          <LinkedTile
            to="/orders?status=Refused"
            label={t.home.refusalRate}
            value={`${(r.rate * 100).toFixed(1).replace(".", ",")} %`}
            hint={t.home.refusalDetail(r.refused, r.delivered)}
            tone={r.rate >= 0.15 ? "danger" : r.rate >= 0.08 ? "warning" : "neutral"}
          />
        </div>

        <Card>
          <CardContent className="pt-4">
            {top.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t.home.noSales}</p>
            ) : (
              <BarList title={t.home.topSellers} data={top} onSelect={isAdmin ? (s) => navigate(`/catalog/fragrances/${s.key}`) : undefined} />
            )}
          </CardContent>
        </Card>
        <p className="text-xs text-muted-foreground">
          {num(d.today.confirmed)} {t.home.confirmedToday} · {num(d.today.cancelled)} {t.home.cancelledToday}
        </p>
      </div>
    </>
  );
}
