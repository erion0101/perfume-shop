import { zodResolver } from "@hookform/resolvers/zod";
import { Badge, Button, Card, CardContent, CardHeader, CardTitle, Dialog, ErrorState, formatMoney, LoadingState, MoneyField, StatusBadge, TextField, toast } from "@perfumeshop/ui";
import { ArrowLeft, Banknote } from "lucide-react";
import { useState, type ReactNode } from "react";
import { Controller, useForm } from "react-hook-form";
import { Link, useParams } from "react-router";
import { z } from "zod";
import { PrintButton } from "@/components/DocumentDialog";
import { PageHeader } from "@/components/PageHeader";
import { usePaymentForOrder } from "@/features/payments/api";
import { useManualCash, useShipment, useShipmentStep, type Shipment, type ShipmentStep } from "@/features/shipping/api";
import { receiptBadges, shipmentBadges } from "@/features/statuses";
import { formatDateTime, label, t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { HandoverOverrideDialog, missingParcels } from "./HandoverPage";

/** Next steps a staff member may record by hand, per status (the API enforces the same rules). */
const nextSteps: Record<string, { step: ShipmentStep; label: string; hint?: string; destructive?: boolean }[]> = {
  Created: [{ step: "handed-over", label: t.shipments.markHandedOver }],
  HandedOver: [
    { step: "in-transit", label: t.shipments.markInTransit },
    { step: "delivered", label: t.shipments.markDelivered },
    { step: "refused", label: t.shipments.markRefused, hint: t.shipments.refusedHint, destructive: true },
  ],
  InTransit: [
    { step: "delivered", label: t.shipments.markDelivered },
    { step: "refused", label: t.shipments.markRefused, hint: t.shipments.refusedHint, destructive: true },
  ],
  Refused: [{ step: "returned", label: t.shipments.markReturned, hint: t.shipments.returnedHint }],
};

function StepDialog({ shipment, step, open, onOpenChange }: { shipment: Shipment; step: (typeof nextSteps)[string][number]; open: boolean; onOpenChange: (open: boolean) => void }) {
  const move = useShipmentStep();
  const [note, setNote] = useState("");
  const [missing, setMissing] = useState<string[] | null>(null);

  async function run(overrideReason?: string) {
    try {
      await move.mutateAsync({ id: shipment.id, step: step.step, note, overrideReason });
      toast.success(t.shipments.stepDone);
      setMissing(null);
      onOpenChange(false);
    } catch (error) {
      const parcels = missingParcels(error);
      if (parcels) {
        setMissing(parcels); // ask for the override reason
        return;
      }

      toast.error(describeError(error));
      throw error;
    }
  }

  return (
    <>
      <Dialog
        open={open && missing === null}
        onOpenChange={onOpenChange}
        title={step.label}
        description={step.hint}
        footer={
          <Button variant={step.destructive ? "destructive" : "default"} disabled={move.isPending} onClick={() => void run().catch(() => undefined)}>
            {step.label}
          </Button>
        }
      >
        <TextField label={t.shipments.stepNote} value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} />
      </Dialog>
      <HandoverOverrideDialog
        open={missing !== null}
        onOpenChange={(next) => {
          if (!next) {
            setMissing(null);
            onOpenChange(false);
          }
        }}
        parcels={missing ?? []}
        onOverride={(reason) => run(reason)}
      />
    </>
  );
}

const cashSchema = (cod: number) =>
  z
    .object({ amount: z.number({ error: t.shipments.cashAmount }).min(0).nullable().refine((v) => v !== null, t.shipments.cashAmount), note: z.string().max(500) })
    .refine((v) => v.amount === cod || v.note.trim().length > 0, { path: ["note"], message: t.shipments.cashNoteRequired });

export function ManualCashDialog({ shipment, open, onOpenChange }: { shipment: Shipment; open: boolean; onOpenChange: (open: boolean) => void }) {
  const cash = useManualCash();
  const schema = cashSchema(shipment.codAmount);
  const { control, register, handleSubmit, formState } = useForm<z.input<typeof schema>, unknown, z.output<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { amount: shipment.codAmount, note: "" },
  });
  const onSubmit = handleSubmit(async (v) => {
    try {
      await cash.mutateAsync({ id: shipment.id, amount: v.amount!, note: v.note.trim() });
      toast.success(t.shipments.cashSaved);
      onOpenChange(false);
    } catch (error) {
      toast.error(describeError(error));
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={`${t.shipments.cashTitle} · ${shipment.trackingNumber}`}
      description={t.shipments.cashHint}
      footer={
        <Button type="submit" form={`cash-${shipment.id}`} disabled={formState.isSubmitting}>
          {t.common.save}
        </Button>
      }
    >
      <form id={`cash-${shipment.id}`} className="grid gap-4" onSubmit={(e) => void onSubmit(e)} noValidate>
        <p className="text-sm">
          {t.shipments.cod}: <strong>{formatMoney(shipment.codAmount)}</strong>
        </p>
        <Controller control={control} name="amount" render={({ field, fieldState }) => <MoneyField label={t.shipments.cashAmount} required error={fieldState.error?.message} {...field} />} />
        <TextField label={t.shipments.cashNote} error={formState.errors.note?.message} {...register("note")} />
      </form>
    </Dialog>
  );
}

function Row({ name, children }: { name: string; children: ReactNode }) {
  return (
    <div className="flex justify-between gap-3 py-1 text-sm">
      <dt className="text-muted-foreground">{name}</dt>
      <dd className="text-right">{children}</dd>
    </div>
  );
}

export function ShipmentDetailPage() {
  const { id = "" } = useParams();
  const shipment = useShipment(id);
  const payment = usePaymentForOrder(shipment.data?.orderId ?? "");
  const [step, setStep] = useState<(typeof nextSteps)[string][number] | null>(null);
  const [cashOpen, setCashOpen] = useState(false);

  if (shipment.isLoading) {
    return <LoadingState />;
  }

  if (shipment.error || !shipment.data) {
    return <ErrorState message={describeError(shipment.error)} onRetry={() => void shipment.refetch()} />;
  }

  const s = shipment.data;
  const sale = payment.data?.receipts.find((r) => r.kind === "Sale");

  return (
    <>
      <PageHeader
        back={
          <Link to="/shipping/shipments" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
            <ArrowLeft className="size-4" /> {t.shipments.back}
          </Link>
        }
        title={t.shipments.detailsTitle(s.trackingNumber)}
        description={
          <span className="flex flex-wrap items-center gap-2">
            <StatusBadge status={s.status} map={shipmentBadges} />
            <Link className="underline-offset-4 hover:underline" to={`/orders/${s.orderId}`}>
              {s.orderNumber}
            </Link>
            · {s.courierName}
          </span>
        }
        actions={
          <>
            {(nextSteps[s.status] ?? []).map((next) => (
              <Button key={next.step} variant={next.destructive ? "outline" : "default"} onClick={() => setStep(next)}>
                {next.label}
              </Button>
            ))}
            {s.status === "Delivered" && !s.settledAtUtc ? (
              <Button variant="outline" onClick={() => setCashOpen(true)}>
                <Banknote /> {t.shipments.cashCollected}
              </Button>
            ) : null}
            <PrintButton document={{ kind: "packing-slip", shipmentId: s.id }} title={t.shipments.packingSlip} label={t.shipments.printSlip} />
            {sale?.status === "Issued" ? <PrintButton document={{ kind: "receipt", receiptId: sale.id }} title={sale.internalNumber} label={t.shipments.printReceipt} /> : null}
          </>
        }
      />

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>{t.shipments.recipient}</CardTitle>
          </CardHeader>
          <CardContent>
            <dl>
              <Row name={t.shipments.recipient}>{s.recipient.fullName}</Row>
              <Row name={t.orders.phone}>
                <a className="text-primary underline-offset-4 hover:underline" href={`tel:${s.recipient.phone}`}>
                  {s.recipient.phone}
                </a>
              </Row>
              <Row name={t.orderDetail.address}>
                {s.recipient.street}, {s.recipient.city}
              </Row>
              <Row name={t.shipments.cod}>
                <strong>{formatMoney(s.codAmount)}</strong>
              </Row>
              <Row name={t.shipments.parcels}>{s.parcelCount}</Row>
              {s.weightGrams ? <Row name={t.shipments.weight}>{s.weightGrams}</Row> : null}
              <Row name={t.settlements.collected}>{s.settledAtUtc ? `${formatMoney(s.collectedAmount)} · ${t.shipments.settled}` : t.shipments.notSettled}</Row>
              {s.codFee != null ? <Row name={t.settlements.fee}>{formatMoney(s.codFee)}</Row> : null}
              <Row name={t.orderDetail.receipts}>{sale ? <StatusBadge status={sale.status} map={receiptBadges} /> : t.common.none}</Row>
            </dl>
            {s.receiptOverrideReason ? (
              <p className="mt-2 rounded-md bg-amber-50 p-2 text-sm">
                <Badge tone="warning">{t.shipments.receiptOverride}</Badge> {s.receiptOverrideReason}
              </p>
            ) : null}
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>{t.shipments.timeline}</CardTitle>
          </CardHeader>
          <CardContent>
            <ol className="grid gap-2 text-sm">
              {s.timeline.map((e, i) => (
                <li key={`${e.atUtc}-${i}`} className="flex flex-wrap gap-x-2">
                  <span className="text-muted-foreground tabular-nums">{formatDateTime(e.atUtc)}</span>
                  <StatusBadge status={e.to} map={shipmentBadges} />
                  <span className="text-muted-foreground">{label(t.shipments.source, e.source)}</span>
                  {e.note ? <span>· {e.note}</span> : null}
                </li>
              ))}
            </ol>
          </CardContent>
        </Card>
      </div>

      {step ? <StepDialog shipment={s} step={step} open onOpenChange={(open) => !open && setStep(null)} /> : null}
      {cashOpen ? <ManualCashDialog shipment={s} open onOpenChange={setCashOpen} /> : null}
    </>
  );
}
