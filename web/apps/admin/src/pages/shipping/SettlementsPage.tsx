import { Button, Card, CardContent, CardHeader, CardTitle, DataTable, ErrorState, formatMoney, LoadingState, Select, StatusBadge, TextField, toast } from "@perfumeshop/ui";
import { ArrowLeft, Banknote, Upload } from "lucide-react";
import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { CsvFileInput } from "@/components/CsvFileInput";
import { PageHeader } from "@/components/PageHeader";
import { useCouriers, useImportSettlement, useSettlement, useSettlements, useShipments, type Settlement } from "@/features/shipping/api";
import { settlementLineBadges } from "@/features/statuses";
import { formatDate, formatDateTime, label, t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { ManualCashDialog } from "./ShipmentDetailPage";

/** Manual booking for a mismatched line: finds the shipment by tracking number, then the cash dialog. */
function BookCashButton({ tracking }: { tracking: string }) {
  const [open, setOpen] = useState(false);
  const shipments = useShipments({ status: "", courierId: "", tracking, orderNumber: "", page: 1, pageSize: 5 }, open);
  const shipment = shipments.data?.items.find((s) => s.trackingNumber === tracking);
  return (
    <>
      <Button variant="outline" size="sm" onClick={() => setOpen(true)}>
        <Banknote /> {t.settlements.bookCash}
      </Button>
      {open && shipment ? <ManualCashDialog shipment={shipment} open onOpenChange={setOpen} /> : null}
    </>
  );
}

export function SettlementReport({ settlement }: { settlement: Settlement }) {
  const problems = settlement.lines.filter((l) => l.status !== "Matched");
  return (
    <div className="grid gap-4">
      {settlement.replayed ? <p className="rounded-md bg-amber-50 p-2 text-sm">{t.settlements.replayed}</p> : null}
      <dl className="grid grid-cols-2 gap-2 sm:grid-cols-5">
        {[
          [t.settlements.collected, formatMoney(settlement.totalCollected)],
          [t.settlements.fees, formatMoney(settlement.totalFees)],
          [t.settlements.net, formatMoney(settlement.netPaid)],
          [t.settlements.matched, `${settlement.matchedCount} · ${formatMoney(settlement.matchedAmount)}`],
          [t.settlements.mismatches, String(problems.length)],
        ].map(([name, value]) => (
          <div key={name} className="rounded-lg border border-border bg-background p-3">
            <dt className="text-xs text-muted-foreground">{name}</dt>
            <dd className="font-semibold tabular-nums">{value}</dd>
          </div>
        ))}
      </dl>

      <Card>
        <CardHeader>
          <CardTitle>{t.settlements.mismatches}</CardTitle>
          <div className="flex flex-wrap gap-1">
            {Object.entries(settlement.mismatches).map(([status, count]) => (
              <StatusBadge key={status} status={status} map={{ [status]: { label: `${label(t.settlements.lineStatus, status)}: ${count}`, tone: settlementLineBadges[status]?.tone ?? "neutral" } }} />
            ))}
          </div>
        </CardHeader>
        <CardContent className="overflow-x-auto">
          {problems.length === 0 ? (
            <p className="text-sm text-emerald-700">{t.settlements.noMismatches}</p>
          ) : (
            <table className="w-full min-w-[48rem] text-sm" aria-label={t.settlements.mismatches}>
              <thead>
                <tr className="border-b border-border text-left text-muted-foreground">
                  <th className="px-2 py-1.5 font-medium">{t.settlements.line}</th>
                  <th className="px-2 py-1.5 font-medium">{t.settlements.tracking}</th>
                  <th className="px-2 py-1.5 font-medium">{t.settlements.order}</th>
                  <th className="px-2 py-1.5 text-right font-medium">{t.settlements.amount}</th>
                  <th className="px-2 py-1.5 text-right font-medium">{t.settlements.expected}</th>
                  <th className="px-2 py-1.5 text-right font-medium">{t.settlements.fee}</th>
                  <th className="px-2 py-1.5 font-medium">{t.settlements.status}</th>
                  <th className="px-2 py-1.5 font-medium">{t.settlements.note}</th>
                  <th className="px-2 py-1.5" />
                </tr>
              </thead>
              <tbody>
                {problems.map((l) => (
                  <tr key={l.lineNumber} className="border-b border-border bg-red-50/60 last:border-0">
                    <td className="px-2 py-1.5 tabular-nums">{l.lineNumber}</td>
                    <td className="px-2 py-1.5 font-medium">{l.trackingNumber}</td>
                    <td className="px-2 py-1.5">{l.orderNumber ?? t.common.none}</td>
                    <td className="px-2 py-1.5 text-right tabular-nums">{formatMoney(l.collectedAmount)}</td>
                    <td className="px-2 py-1.5 text-right tabular-nums">{formatMoney(l.expectedAmount)}</td>
                    <td className="px-2 py-1.5 text-right tabular-nums">
                      {formatMoney(l.fee)}
                      {l.expectedFee != null && l.expectedFee !== l.fee ? <span className="block text-xs text-muted-foreground">{formatMoney(l.expectedFee)}</span> : null}
                    </td>
                    <td className="px-2 py-1.5">
                      <StatusBadge status={l.status} map={settlementLineBadges} />
                    </td>
                    <td className="px-2 py-1.5 text-muted-foreground">{l.note ?? ""}</td>
                    <td className="px-2 py-1.5">{l.status === "AmountMismatch" ? <BookCashButton tracking={l.trackingNumber} /> : null}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </CardContent>
      </Card>

      {settlement.deliveredButUnpaid.length > 0 ? (
        <Card>
          <CardHeader>
            <CardTitle>{t.settlements.unpaid}</CardTitle>
          </CardHeader>
          <CardContent>
            <ul className="grid gap-1 text-sm">
              {settlement.deliveredButUnpaid.map((u) => (
                <li key={u.shipmentId} className="flex flex-wrap justify-between gap-2">
                  <Link className="font-medium underline-offset-4 hover:underline" to={`/shipping/shipments/${u.shipmentId}`}>
                    {u.trackingNumber} · {u.orderNumber}
                  </Link>
                  <span className="tabular-nums text-destructive">
                    {formatMoney(u.codAmount)} · {formatDate(u.deliveredAtUtc)} · {u.daysSinceDelivery} {t.settlements.days}
                  </span>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      ) : null}
    </div>
  );
}

export function SettlementsPage() {
  const navigate = useNavigate();
  const couriers = useCouriers();
  const upload = useImportSettlement();
  const [courierId, setCourierId] = useState("");
  const [reference, setReference] = useState("");
  const [file, setFile] = useState<{ name: string; text: string } | null>(null);
  const [result, setResult] = useState<Settlement | null>(null);
  const settlements = useSettlements(courierId);
  const list = settlements.data ?? [];

  async function submit() {
    try {
      const settlement = await upload.mutateAsync({ courierId, reference: reference.trim(), csv: file!.text });
      setResult(settlement);
      toast.success(t.settlements.imported);
    } catch (error) {
      toast.error(describeError(error));
    }
  }

  return (
    <>
      <PageHeader title={t.settlements.title} description={t.settlements.intro} />
      <div className="grid gap-4">
        <Card>
          <CardContent className="grid gap-4">
            <div className="grid gap-4 sm:grid-cols-2">
              <label className="grid gap-1.5 text-sm font-medium">
                {t.settlements.courier}
                <Select aria-label={t.settlements.courier} value={courierId} onChange={(e) => setCourierId(e.target.value)}>
                  <option value="">{t.common.choose}</option>
                  {(couriers.data ?? []).map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                    </option>
                  ))}
                </Select>
              </label>
              <TextField label={t.settlements.reference} hint={t.settlements.referenceHint} required value={reference} onChange={(e) => setReference(e.target.value)} maxLength={100} />
            </div>
            <CsvFileInput
              label={t.settlements.file}
              onLoaded={(loaded) => {
                setFile(loaded);
                setResult(null);
              }}
            />
            <div>
              <Button disabled={!courierId || !file || reference.trim() === "" || upload.isPending} onClick={() => void submit()}>
                <Upload /> {t.settlements.upload}
              </Button>
            </div>
          </CardContent>
        </Card>

        {result ? <SettlementReport settlement={result} /> : null}

        <h2 className="text-lg font-semibold">{t.settlements.list}</h2>
        <DataTable<Settlement>
          caption={t.settlements.list}
          columns={[
            { key: "reference", header: t.settlements.reference, cell: (s) => <span className="font-medium">{s.reference}</span> },
            { key: "courier", header: t.settlements.courier, cell: (s) => s.courierName },
            { key: "imported", header: t.settlements.importedAt, cell: (s) => formatDateTime(s.importedAtUtc), hideOnMobile: true },
            { key: "collected", header: t.settlements.collected, cell: (s) => formatMoney(s.totalCollected), className: "text-right tabular-nums" },
            { key: "net", header: t.settlements.net, cell: (s) => formatMoney(s.netPaid), className: "text-right tabular-nums" },
            {
              key: "mismatches",
              header: t.settlements.mismatches,
              cell: (s) => {
                const count = Object.values(s.mismatches).reduce((a, b) => a + b, 0);
                return <span className={count > 0 ? "font-semibold text-destructive" : ""}>{count}</span>;
              },
              className: "text-right",
            },
          ]}
          rows={settlements.data}
          rowKey={(s) => s.id}
          total={list.length}
          page={1}
          pageSize={Math.max(list.length, 1)}
          onPageChange={() => {}}
          isLoading={settlements.isLoading}
          error={settlements.error ? describeError(settlements.error) : null}
          onRetry={() => void settlements.refetch()}
          emptyTitle={t.settlements.empty}
          onRowClick={(s) => navigate(`/shipping/settlements/${s.id}`)}
        />
      </div>
    </>
  );
}

export function SettlementDetailPage() {
  const { id = "" } = useParams();
  const settlement = useSettlement(id);
  if (settlement.isLoading) {
    return <LoadingState />;
  }

  if (settlement.error || !settlement.data) {
    return <ErrorState message={describeError(settlement.error)} onRetry={() => void settlement.refetch()} />;
  }

  const s = settlement.data;
  return (
    <>
      <PageHeader
        back={
          <Link to="/shipping/settlements" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
            <ArrowLeft className="size-4" /> {t.settlements.back}
          </Link>
        }
        title={t.settlements.detailTitle(s.reference)}
        description={`${s.courierName} · ${formatDateTime(s.importedAtUtc)}`}
      />
      <SettlementReport settlement={{ ...s, replayed: false }} />
    </>
  );
}
