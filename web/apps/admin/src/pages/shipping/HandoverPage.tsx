import { zodResolver } from "@hookform/resolvers/zod";
import { Button, Card, CardContent, ConfirmDialog, Dialog, EmptyState, ErrorState, LoadingState, Select, TextareaField, TextField, toast } from "@perfumeshop/ui";
import { useQuery } from "@tanstack/react-query";
import { Download, Printer, Truck } from "lucide-react";
import { useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { Link } from "react-router";
import { z } from "zod";
import { PageHeader } from "@/components/PageHeader";
import { useCouriers, useHandover } from "@/features/shipping/api";
import { t } from "@/i18n";
import { downloadManifestCsv, fetchDocument } from "@/lib/documents";
import { ApiError, describeError } from "@/lib/problem";

const overrideSchema = z.object({ reason: z.string().trim().min(5, t.handover.overrideReasonRequired).max(500, t.handover.overrideReasonRequired) });

/** The 409 "no issued fiscal receipt" list, and the override with a required reason (stored and logged). */
export function HandoverOverrideDialog({
  open,
  onOpenChange,
  parcels,
  onOverride,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  parcels: string[];
  onOverride: (reason: string) => Promise<unknown>;
}) {
  const { register, handleSubmit, formState, reset } = useForm({ resolver: zodResolver(overrideSchema), defaultValues: { reason: "" } });
  const onSubmit = handleSubmit(async ({ reason }) => {
    try {
      await onOverride(reason.trim());
      reset();
    } catch {
      // the caller showed the error
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={t.handover.missingTitle}
      description={t.handover.missingIntro}
      footer={
        <>
          <Button asChild variant="outline">
            <Link to="/payments/receipts?status=Failed">{t.handover.retryReceipts}</Link>
          </Button>
          <Button type="submit" form="override-form" variant="destructive" disabled={formState.isSubmitting}>
            {t.handover.override}
          </Button>
        </>
      }
    >
      <ul className="grid gap-1 rounded-md border border-red-200 bg-red-50 p-3 text-sm" aria-label={t.handover.missingTitle}>
        {parcels.map((p) => (
          <li key={p} className="font-medium">
            {p}
          </li>
        ))}
      </ul>
      <form id="override-form" onSubmit={(e) => void onSubmit(e)} noValidate>
        <TextareaField label={t.handover.overrideReason} hint={t.handover.overrideHint} required rows={3} error={formState.errors.reason?.message} {...register("reason")} />
      </form>
    </Dialog>
  );
}

/** Parcels the API listed in the 409 (Shipping.Shipment.ReceiptMissing). */
export function missingParcels(error: unknown): string[] | null {
  if (!(error instanceof ApiError) || error.code !== "Shipping.Shipment.ReceiptMissing") {
    return null;
  }

  const parcels = (error.problem.parcels as { trackingNumber: string; orderNumber: string; receipt: string }[] | undefined) ?? [];
  return parcels.map((p) => `${p.trackingNumber} · ${p.orderNumber} · ${t.enums.receiptStatus[p.receipt] ?? p.receipt}`);
}

const today = () => new Date().toLocaleDateString("sv-SE", { timeZone: "Europe/Belgrade" });

export function HandoverPage() {
  const couriers = useCouriers();
  const handover = useHandover();
  const [courierId, setCourierId] = useState("");
  const [date, setDate] = useState(today);
  const [missing, setMissing] = useState<string[] | null>(null);
  const frame = useRef<HTMLIFrameElement>(null);
  const manifest = useQuery({
    queryKey: ["shipping", "manifest", courierId, date],
    enabled: courierId !== "",
    queryFn: () => fetchDocument({ kind: "manifest", courierId, date }),
  });

  async function handOver(overrideReason?: string) {
    try {
      const result = await handover.mutateAsync({ courierId, date, overrideReason });
      setMissing(null);
      if (result.handedOver === 0) {
        toast.info(t.handover.nothing);
      } else {
        toast.success([t.handover.done(result.handedOver), result.withoutReceipt.length ? t.handover.doneWithout(result.withoutReceipt.length) : ""].join(" "));
      }

      void manifest.refetch();
    } catch (error) {
      const parcels = missingParcels(error);
      if (parcels) {
        setMissing(parcels);
        return;
      }

      toast.error(describeError(error));
      throw error;
    }
  }

  return (
    <>
      <PageHeader title={t.handover.title} description={t.handover.intro} />
      <Card className="mb-4">
        <CardContent className="flex flex-col gap-3 sm:flex-row sm:items-end">
          <label className="grid gap-1.5 text-sm font-medium sm:w-64">
            {t.handover.courier}
            <Select aria-label={t.handover.courier} value={courierId} onChange={(e) => setCourierId(e.target.value)}>
              <option value="">{t.common.choose}</option>
              {(couriers.data ?? [])
                .filter((c) => c.isSelectable)
                .map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
            </Select>
          </label>
          <TextField label={t.handover.date} type="date" value={date} onChange={(e) => setDate(e.target.value)} className="sm:w-44" />
          <div className="flex flex-wrap gap-2 sm:ml-auto">
            <Button variant="outline" disabled={!manifest.data} onClick={() => frame.current?.contentWindow?.print()}>
              <Printer /> {t.handover.print}
            </Button>
            <Button
              variant="outline"
              disabled={!courierId}
              onClick={() => void downloadManifestCsv(courierId, date).catch((error: unknown) => toast.error(describeError(error)))}
            >
              <Download /> {t.handover.csv}
            </Button>
            <ConfirmDialog
              trigger={
                <Button disabled={!courierId}>
                  <Truck /> {t.handover.handOver}
                </Button>
              }
              title={t.handover.handOverConfirm}
              description={t.handover.handOverHint}
              confirmLabel={t.handover.handOver}
              onConfirm={() => handOver().catch((error: unknown) => (missingParcels(error) ? undefined : Promise.reject(error)))}
            />
          </div>
        </CardContent>
      </Card>

      {!courierId ? (
        <EmptyState title={t.handover.chooseCourier} />
      ) : manifest.isLoading ? (
        <LoadingState />
      ) : manifest.error ? (
        <ErrorState message={describeError(manifest.error)} onRetry={() => void manifest.refetch()} />
      ) : (
        <iframe ref={frame} title={t.handover.preview} srcDoc={manifest.data} sandbox="allow-same-origin allow-modals" className="h-[70dvh] w-full rounded-md border border-border bg-white" />
      )}

      <HandoverOverrideDialog open={missing !== null} onOpenChange={(open) => !open && setMissing(null)} parcels={missing ?? []} onOverride={(reason) => handOver(reason)} />
    </>
  );
}
