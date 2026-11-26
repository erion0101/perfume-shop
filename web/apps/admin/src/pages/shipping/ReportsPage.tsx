import { Card, CardContent, CardHeader, CardTitle, EmptyState, formatMoney, LoadingState, TextField } from "@perfumeshop/ui";
import { useState, type ReactNode } from "react";
import { Link } from "react-router";
import { PageHeader } from "@/components/PageHeader";
import { useCodHeldReport, useInTransitReport, useRefusalReport } from "@/features/shipping/api";
import { formatDate, label, t } from "@/i18n";

const percent = (rate: number) => `${(rate * 100).toFixed(1).replace(".", ",")} %`;
const th = "px-2 py-1.5 text-left font-medium text-muted-foreground";
const td = "px-2 py-1.5";

function Table({ label: name, head, children }: { label: string; head: string[]; children: ReactNode }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[32rem] text-sm" aria-label={name}>
        <thead>
          <tr className="border-b border-border">
            {head.map((h) => (
              <th key={h} className={th}>
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}

export function ReportsPage() {
  const [days, setDays] = useState("3");
  const inTransit = useInTransitReport(/^\d+$/.test(days) ? Number(days) : 3);
  const refusals = useRefusalReport();
  const held = useCodHeldReport();

  return (
    <>
      <PageHeader title={t.reports.title} />
      <div className="grid gap-4">
        <Card>
          <CardHeader>
            <CardTitle>{t.reports.codHeld}</CardTitle>
          </CardHeader>
          <CardContent>
            {held.isLoading ? (
              <LoadingState />
            ) : (held.data ?? []).every((c) => c.parcels === 0) ? (
              <EmptyState title={t.reports.noneHeld} />
            ) : (
              <Table label={t.reports.codHeld} head={[t.shipments.courier, t.reports.parcels, t.reports.held, t.reports.overdue, t.reports.oldest]}>
                {held.data!.map((c) => (
                  <tr key={c.courierId} className={`border-b border-border last:border-0 ${c.overdueParcels > 0 ? "bg-red-50" : ""}`}>
                    <td className={`${td} font-medium`}>{c.courierName}</td>
                    <td className={`${td} tabular-nums`}>{c.parcels}</td>
                    <td className={`${td} tabular-nums`}>{formatMoney(c.heldAmount)}</td>
                    <td className={`${td} tabular-nums ${c.overdueParcels > 0 ? "font-semibold text-destructive" : ""}`}>
                      {c.overdueParcels} · {formatMoney(c.overdueAmount)}
                    </td>
                    <td className={td}>{formatDate(c.oldestDeliveryUtc)}</td>
                  </tr>
                ))}
              </Table>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>{t.reports.inTransit}</CardTitle>
            <TextField label={t.reports.olderThan} inputMode="numeric" value={days} onChange={(e) => setDays(e.target.value)} className="w-32" />
          </CardHeader>
          <CardContent>
            {inTransit.isLoading ? (
              <LoadingState />
            ) : (inTransit.data ?? []).length === 0 ? (
              <EmptyState title={t.reports.noneInTransit} />
            ) : (
              <Table label={t.reports.inTransit} head={[t.shipments.tracking, t.shipments.number, t.shipments.courier, t.shipments.status, t.orders.city, t.shipments.cod, t.reports.days]}>
                {inTransit.data!.map((s) => (
                  <tr key={s.shipmentId} className="border-b border-border last:border-0">
                    <td className={`${td} font-medium`}>
                      <Link className="underline-offset-4 hover:underline" to={`/shipping/shipments/${s.shipmentId}`}>
                        {s.trackingNumber}
                      </Link>
                    </td>
                    <td className={td}>{s.orderNumber}</td>
                    <td className={td}>{s.courierName}</td>
                    <td className={td}>{label(t.enums.shipmentStatus, s.status)}</td>
                    <td className={td}>{s.city}</td>
                    <td className={`${td} tabular-nums`}>{formatMoney(s.codAmount)}</td>
                    <td className={`${td} font-semibold tabular-nums text-destructive`}>{s.days}</td>
                  </tr>
                ))}
              </Table>
            )}
          </CardContent>
        </Card>

        <div className="grid gap-4 lg:grid-cols-2">
          <Card>
            <CardHeader>
              <CardTitle>
                {t.reports.refusals} · {t.reports.byCourier}
              </CardTitle>
            </CardHeader>
            <CardContent>
              {(refusals.data?.byCourier.length ?? 0) === 0 ? (
                <EmptyState title={t.reports.noRefusals} />
              ) : (
                <Table label={t.reports.byCourier} head={[t.shipments.courier, t.reports.delivered, t.reports.refused, t.reports.rate]}>
                  {refusals.data!.byCourier.map((c) => (
                    <tr key={c.courierId} className="border-b border-border last:border-0">
                      <td className={`${td} font-medium`}>{c.courierName}</td>
                      <td className={`${td} tabular-nums`}>{c.delivered}</td>
                      <td className={`${td} tabular-nums`}>{c.refused}</td>
                      <td className={`${td} tabular-nums`}>{percent(c.refusalRate)}</td>
                    </tr>
                  ))}
                </Table>
              )}
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>{t.reports.byCustomer}</CardTitle>
            </CardHeader>
            <CardContent>
              {(refusals.data?.byCustomer.length ?? 0) === 0 ? (
                <EmptyState title={t.reports.noRefusals} />
              ) : (
                <Table label={t.reports.byCustomer} head={[t.orders.customer, t.orders.phone, t.reports.shipments, t.reports.refused, t.reports.rate]}>
                  {refusals.data!.byCustomer.map((c) => (
                    <tr key={c.customerId} className="border-b border-border last:border-0">
                      <td className={`${td} font-medium`}>{c.lastName}</td>
                      <td className={td}>
                        <a className="text-primary underline-offset-4 hover:underline" href={`tel:${c.phone}`}>
                          {c.phone}
                        </a>
                      </td>
                      <td className={`${td} tabular-nums`}>{c.shipments}</td>
                      <td className={`${td} tabular-nums text-destructive`}>{c.refused}</td>
                      <td className={`${td} tabular-nums`}>{percent(c.refusalRate)}</td>
                    </tr>
                  ))}
                </Table>
              )}
            </CardContent>
          </Card>
        </div>
      </div>
    </>
  );
}
