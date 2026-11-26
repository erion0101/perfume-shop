import { zodResolver } from "@hookform/resolvers/zod";
import { Button, Card, CardContent, CardHeader, CardTitle, Dialog, EmptyState, ErrorState, formatMoney, LoadingState, SelectField, StatusBadge, TextField, toast } from "@perfumeshop/ui";
import { ArrowLeft, PackagePlus, SlidersHorizontal } from "lucide-react";
import { useState, type ReactNode } from "react";
import { useForm } from "react-hook-form";
import { Link, useParams } from "react-router";
import { z } from "zod";
import { PageHeader } from "@/components/PageHeader";
import { useAdjustStock, useStockItem, type StockItemDetails } from "@/features/inventory/api";
import { formatDate, formatDateTime, label, t } from "@/i18n";
import { applyFieldErrors, describeError } from "@/lib/problem";
import { stockBadges, stockState, variantName } from "./StockPage";

const adjustSchema = z.object({
  quantity: z.string().regex(/^[+-]?\d+$/, t.stockItem.adjustQuantityInvalid).refine((v) => Number(v) !== 0, t.stockItem.adjustQuantityInvalid),
  reason: z.string().trim().min(3, t.stockItem.adjustReasonRequired).max(500, t.stockItem.adjustReasonRequired),
  batchId: z.string(),
});

type AdjustForm = z.infer<typeof adjustSchema>;

function AdjustDialog({ details, open, onOpenChange }: { details: StockItemDetails; open: boolean; onOpenChange: (open: boolean) => void }) {
  const adjust = useAdjustStock(details.stock.variantId);
  const { register, handleSubmit, formState, setError, reset } = useForm<AdjustForm>({
    resolver: zodResolver(adjustSchema),
    defaultValues: { quantity: "", reason: "", batchId: "" },
  });

  const onSubmit = handleSubmit(async (v) => {
    try {
      await adjust.mutateAsync({ quantity: Number(v.quantity), reason: v.reason.trim(), batchId: v.batchId || null });
      toast.success(t.stockItem.adjusted);
      reset();
      onOpenChange(false);
    } catch (error) {
      applyFieldErrors(error, setError, ["quantity", "reason", "batchId"]);
      toast.error(describeError(error));
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={t.stockItem.adjustTitle}
      description={t.stockItem.adjustHint}
      footer={
        <Button type="submit" form="adjust-form" disabled={formState.isSubmitting}>
          {formState.isSubmitting ? t.common.saving : t.common.save}
        </Button>
      }
    >
      <form id="adjust-form" className="grid gap-4" onSubmit={(e) => void onSubmit(e)} noValidate>
        <TextField label={t.stockItem.adjustQuantity} inputMode="numeric" placeholder="-1" required error={formState.errors.quantity?.message} {...register("quantity")} />
        <SelectField
          label={t.stockItem.adjustBatch}
          placeholder={t.common.choose}
          options={details.batches.map((b) => ({ value: b.id, label: `${b.batchCode} · ${t.stockItem.remaining} ${b.quantityRemaining}/${b.quantityReceived}` }))}
          {...register("batchId")}
        />
        <TextField label={t.stockItem.adjustReason} required error={formState.errors.reason?.message} {...register("reason")} />
      </form>
    </Dialog>
  );
}

function Section({ title, empty, children }: { title: string; empty: string; children: ReactNode[] }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
      </CardHeader>
      <CardContent className="overflow-x-auto">{children.length === 0 ? <EmptyState title={empty} /> : children}</CardContent>
    </Card>
  );
}

const th = "px-2 py-1.5 text-left font-medium text-muted-foreground";
const td = "px-2 py-1.5 align-top";

export function StockItemPage() {
  const { variantId = "" } = useParams();
  const item = useStockItem(variantId);
  const [adjusting, setAdjusting] = useState(false);

  if (item.isLoading) {
    return <LoadingState />;
  }

  if (item.error || !item.data) {
    return (
      <>
        <ErrorState message={describeError(item.error)} onRetry={() => void item.refetch()} />
        <div className="flex justify-center">
          <Button asChild variant="outline">
            <Link to="/inventory/receive">
              <PackagePlus /> {t.stock.receive}
            </Link>
          </Button>
        </div>
      </>
    );
  }

  const d = item.data;
  const s = d.stock;

  return (
    <>
      <PageHeader
        back={
          <Link to="/inventory" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
            <ArrowLeft className="size-4" /> {t.nav.stock}
          </Link>
        }
        title={variantName(s)}
        description={
          <span className="flex flex-wrap items-center gap-2">
            {s.variant?.sku} <StatusBadge status={stockState(s)} map={stockBadges} />
          </span>
        }
        actions={
          <>
            <Button variant="outline" onClick={() => setAdjusting(true)}>
              <SlidersHorizontal /> {t.stockItem.adjust}
            </Button>
            <Button asChild>
              <Link to="/inventory/receive">
                <PackagePlus /> {t.stock.receive}
              </Link>
            </Button>
          </>
        }
      />

      <dl className="mb-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
        {[
          [t.stock.onHand, s.onHand],
          [t.stock.reserved, s.reserved],
          [t.stock.available, s.available],
          [t.stock.threshold, s.lowStockThreshold],
        ].map(([name, value]) => (
          <div key={name} className="rounded-lg border border-border bg-background p-3">
            <dt className="text-xs text-muted-foreground">{name}</dt>
            <dd className="text-2xl font-semibold tabular-nums">{value}</dd>
          </div>
        ))}
      </dl>

      <div className="grid gap-4">
        <Section title={t.stockItem.batches} empty={t.stockItem.noBatches}>
          {d.batches.length === 0
            ? []
            : [
                <table key="batches" className="w-full min-w-[40rem] text-sm">
                  <thead>
                    <tr className="border-b border-border">
                      <th className={th}>{t.stockItem.batchCode}</th>
                      <th className={th}>{t.stockItem.remaining}</th>
                      <th className={th}>{t.stockItem.unitCost}</th>
                      <th className={th}>{t.stockItem.supplier}</th>
                      <th className={th}>{t.stockItem.invoice}</th>
                      <th className={th}>{t.stockItem.received}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {d.batches.map((b) => (
                      <tr key={b.id} className="border-b border-border last:border-0">
                        <td className={td}>
                          <Link className="font-medium underline-offset-4 hover:underline" to={`/inventory/trace?code=${encodeURIComponent(b.batchCode)}`}>
                            {b.batchCode}
                          </Link>
                        </td>
                        <td className={`${td} tabular-nums`}>
                          {b.quantityRemaining} / {b.quantityReceived}
                        </td>
                        <td className={td}>{formatMoney(b.unitCost)}</td>
                        <td className={td}>{b.supplierName}</td>
                        <td className={td}>
                          {b.supplierInvoiceNumber} · {formatDate(b.supplierInvoiceDate)}
                        </td>
                        <td className={td}>{formatDate(b.receivedOn)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>,
              ]}
        </Section>

        <Section title={t.stockItem.reservations} empty={t.stockItem.noReservations}>
          {d.openReservations.map((r) => (
            <p key={r.orderLineId} className="flex flex-wrap justify-between gap-2 border-b border-border py-1.5 text-sm last:border-0">
              <span>
                <Link className="underline-offset-4 hover:underline" to={`/orders/${r.orderId}`}>
                  {t.stockItem.order}
                </Link>{" "}
                · {r.quantity} · {label(t.enums.allocation, r.status)}
              </span>
              <span className="text-muted-foreground">
                {t.stockItem.expires}: {formatDateTime(r.expiresAtUtc)}
              </span>
            </p>
          ))}
        </Section>

        <Section title={t.stockItem.movements} empty={t.stockItem.noMovements}>
          {d.movements.length === 0
            ? []
            : [
                <table key="movements" className="w-full min-w-[40rem] text-sm">
                  <thead>
                    <tr className="border-b border-border">
                      <th className={th}>{t.stockItem.when}</th>
                      <th className={th}>{t.stockItem.type}</th>
                      <th className={th}>{t.stockItem.quantity}</th>
                      <th className={th}>{t.stockItem.onHandAfter}</th>
                      <th className={th}>{t.stockItem.batchCode}</th>
                      <th className={th}>{t.stockItem.reason}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {d.movements.map((m, index) => (
                      <tr key={`${m.occurredAtUtc}-${index}`} className="border-b border-border last:border-0">
                        <td className={td}>{formatDateTime(m.occurredAtUtc)}</td>
                        <td className={td}>{label(t.enums.movement, m.type)}</td>
                        <td className={`${td} tabular-nums`}>{m.onHandDelta > 0 ? `+${m.onHandDelta}` : m.onHandDelta || m.quantity}</td>
                        <td className={`${td} tabular-nums`}>{m.onHandAfter}</td>
                        <td className={td}>{m.batchCodes ?? t.common.none}</td>
                        <td className={td}>{m.reason ?? (m.orderId ? `${t.stockItem.order} ${m.orderId.slice(0, 8)}` : t.common.none)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>,
              ]}
        </Section>
      </div>
      <AdjustDialog details={d} open={adjusting} onOpenChange={setAdjusting} />
    </>
  );
}
