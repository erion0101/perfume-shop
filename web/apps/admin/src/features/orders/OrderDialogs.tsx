import { zodResolver } from "@hookform/resolvers/zod";
import { Button, Dialog, SelectField, TextareaField, TextField, toast } from "@perfumeshop/ui";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { couriersFor, useCouriers, useCreateShipment, type Shipment } from "@/features/shipping/api";
import { t } from "@/i18n";
import { applyFieldErrors, describeError } from "@/lib/problem";
import { useAddOrderNote, useCancelOrder, type OrderAdmin } from "./api";

const reasonSchema = z.object({ reason: z.string().trim().min(3, t.queue.reasonRequired).max(500, t.queue.reasonRequired) });

export function CancelOrderDialog({ order, open, onOpenChange }: { order: { id: string; number: string }; open: boolean; onOpenChange: (open: boolean) => void }) {
  const cancel = useCancelOrder();
  const { register, handleSubmit, formState, reset } = useForm({ resolver: zodResolver(reasonSchema), defaultValues: { reason: "" } });
  const onSubmit = handleSubmit(async ({ reason }) => {
    try {
      await cancel.mutateAsync({ id: order.id, reason: reason.trim() });
      toast.success(t.queue.cancelled(order.number));
      reset();
      onOpenChange(false);
    } catch (error) {
      toast.error(describeError(error));
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={t.queue.cancelTitle(order.number)}
      footer={
        <Button type="submit" form={`cancel-${order.id}`} variant="destructive" disabled={formState.isSubmitting}>
          {t.queue.cancel}
        </Button>
      }
    >
      <form id={`cancel-${order.id}`} onSubmit={(e) => void onSubmit(e)} noValidate>
        <TextareaField label={t.queue.cancelReason} hint={t.queue.cancelReasonHint} required rows={3} error={formState.errors.reason?.message} {...register("reason")} />
      </form>
    </Dialog>
  );
}

const noteSchema = z.object({ text: z.string().trim().min(1, t.queue.noteRequired).max(1000, t.queue.noteRequired) });

export function OrderNoteDialog({ orderId, open, onOpenChange }: { orderId: string; open: boolean; onOpenChange: (open: boolean) => void }) {
  const add = useAddOrderNote();
  const { register, handleSubmit, formState, reset } = useForm({ resolver: zodResolver(noteSchema), defaultValues: { text: "" } });
  const onSubmit = handleSubmit(async ({ text }) => {
    try {
      await add.mutateAsync({ id: orderId, text: text.trim() });
      toast.success(t.queue.noteSaved);
      reset();
      onOpenChange(false);
    } catch (error) {
      toast.error(describeError(error));
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={t.queue.noteTitle}
      footer={
        <Button type="submit" form={`note-${orderId}`} disabled={formState.isSubmitting}>
          {t.common.save}
        </Button>
      }
    >
      <form id={`note-${orderId}`} onSubmit={(e) => void onSubmit(e)} noValidate>
        <TextareaField label={t.queue.noteText} required rows={4} error={formState.errors.text?.message} {...register("text")} />
      </form>
    </Dialog>
  );
}

export const shipmentSchema = z.object({
  courierId: z.string().min(1, t.createShipment.courierRequired),
  trackingNumber: z.string().trim().min(1, t.createShipment.trackingRequired).max(100, t.createShipment.trackingRequired),
  parcelCount: z.string().regex(/^([1-9]|10)$/, t.createShipment.parcelsInvalid),
  weightGrams: z.string().refine((v) => v.trim() === "" || (/^\d+$/.test(v) && Number(v) >= 1 && Number(v) <= 30_000), t.createShipment.weightInvalid),
  note: z.string().max(500),
});

type ShipmentForm = z.infer<typeof shipmentSchema>;

/** Confirmed order → shipment. Only couriers that are enabled, accept UN1266 and serve the country are offered. */
export function CreateShipmentDialog({
  order,
  open,
  onOpenChange,
  onCreated,
}: {
  order: OrderAdmin;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onCreated: (shipment: Shipment) => void;
}) {
  const couriers = useCouriers();
  const create = useCreateShipment();
  const valid = couriersFor(couriers.data, order.order.deliveryAddress.countryCode);
  const { register, handleSubmit, formState, setError } = useForm<ShipmentForm>({
    resolver: zodResolver(shipmentSchema),
    values: { courierId: valid.length === 1 ? valid[0]!.id : "", trackingNumber: "", parcelCount: "1", weightGrams: "", note: "" },
  });

  const onSubmit = handleSubmit(async (v) => {
    try {
      const shipment = await create.mutateAsync({
        orderId: order.order.id,
        courierId: v.courierId,
        trackingNumber: v.trackingNumber.trim(),
        parcelCount: Number(v.parcelCount),
        weightGrams: v.weightGrams ? Number(v.weightGrams) : null,
        note: v.note.trim() || null,
      });
      toast.success(t.createShipment.created(shipment.trackingNumber));
      onOpenChange(false);
      onCreated(shipment);
    } catch (error) {
      applyFieldErrors(error, setError, ["courierId", "trackingNumber", "parcelCount", "weightGrams", "note"]);
      toast.error(describeError(error));
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={`${t.createShipment.title} · ${order.order.number}`}
      footer={
        <Button type="submit" form="create-shipment" disabled={formState.isSubmitting || valid.length === 0}>
          {t.createShipment.submit}
        </Button>
      }
    >
      <form id="create-shipment" className="grid gap-4" onSubmit={(e) => void onSubmit(e)} noValidate>
        {couriers.data && valid.length === 0 ? (
          <p role="alert" className="rounded-md bg-amber-50 p-3 text-sm">
            {t.createShipment.noCourier}
          </p>
        ) : null}
        <SelectField
          label={t.createShipment.courier}
          hint={t.createShipment.courierHint}
          required
          placeholder={t.common.choose}
          options={valid.map((c) => ({ value: c.id, label: c.name }))}
          error={formState.errors.courierId?.message}
          {...register("courierId")}
        />
        <TextField label={t.createShipment.tracking} hint={t.createShipment.trackingHint} required autoCapitalize="characters" error={formState.errors.trackingNumber?.message} {...register("trackingNumber")} />
        <div className="grid grid-cols-2 gap-4">
          <TextField label={t.createShipment.parcels} inputMode="numeric" required error={formState.errors.parcelCount?.message} {...register("parcelCount")} />
          <TextField label={t.createShipment.weight} inputMode="numeric" error={formState.errors.weightGrams?.message} {...register("weightGrams")} />
        </div>
        <TextField label={t.createShipment.note} error={formState.errors.note?.message} {...register("note")} />
      </form>
    </Dialog>
  );
}
