import { zodResolver } from "@hookform/resolvers/zod";
import { Badge, Button, Checkbox, DataTable, Dialog, formatMoney, MoneyField, SelectField, TextareaField, TextField, toast } from "@perfumeshop/ui";
import { Flame, Pencil, Plus } from "lucide-react";
import { useState } from "react";
import { Controller, useForm, useWatch, type UseFormRegisterReturn } from "react-hook-form";
import { z } from "zod";
import { PageHeader } from "@/components/PageHeader";
import { useCouriers, useSaveCourier, type Courier, type CourierInput } from "@/features/shipping/api";
import { label, options, t } from "@/i18n";
import { useAuth } from "@/lib/auth";
import { applyFieldErrors, describeError } from "@/lib/problem";

const fee = z.number({ error: t.couriers.feeInvalid }).min(0, t.couriers.feeInvalid).max(100, t.couriers.feeInvalid).nullable().refine((v) => v !== null, t.couriers.feeInvalid);

export const courierSchema = z.object({
  name: z.string().trim().min(1, t.couriers.nameRequired).max(100, t.couriers.nameRequired),
  contactName: z.string().max(100),
  contactPhone: z.string().max(30),
  contactEmail: z.union([z.literal(""), z.email(t.couriers.emailInvalid)]),
  enabledCountries: z.array(z.string()).min(1, t.couriers.countriesRequired),
  deliveryFee: fee,
  codFeeFixed: fee,
  codFeePercent: z.string().refine((v) => /^\d+([.,]\d{1,2})?$/.test(v) && Number(v.replace(",", ".")) <= 10, t.couriers.percentInvalid),
  acceptsFlammableGoods: z.boolean(),
  remittanceSchedule: z.string(),
  remittanceDays: z.string().refine((v) => /^\d+$/.test(v) && Number(v) >= 1 && Number(v) <= 60, t.couriers.daysInvalid),
  notes: z.string().max(1000),
  isEnabled: z.boolean(),
});

type CourierForm = z.input<typeof courierSchema>;
type CourierValues = z.output<typeof courierSchema>;

function CountryChecks({ register }: { register: UseFormRegisterReturn }) {
  return (
    <fieldset className="grid gap-1.5">
      <legend className="mb-1 text-sm font-medium">{t.couriers.countries} *</legend>
      <div className="flex flex-wrap gap-4">
        {Object.entries(t.couriers.country).map(([code, name]) => (
          <label key={code} className="flex items-center gap-2 text-sm">
            <Checkbox value={code} {...register} /> {name}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

function CourierDialog({ courier, open, onOpenChange }: { courier: Courier | null; open: boolean; onOpenChange: (open: boolean) => void }) {
  const save = useSaveCourier();
  const { register, control, handleSubmit, formState, setError } = useForm<CourierForm, unknown, CourierValues>({
    resolver: zodResolver(courierSchema),
    values: {
      name: courier?.name ?? "",
      contactName: courier?.contactName ?? "",
      contactPhone: courier?.contactPhone ?? "",
      contactEmail: courier?.contactEmail ?? "",
      enabledCountries: courier?.enabledCountries ?? ["XK"],
      deliveryFee: courier?.deliveryFee ?? 2.5,
      codFeeFixed: courier?.codFeeFixed ?? 0,
      codFeePercent: String(courier?.codFeePercent ?? 0).replace(".", ","),
      acceptsFlammableGoods: courier?.acceptsFlammableGoods ?? false,
      remittanceSchedule: courier?.remittanceSchedule ?? "Weekly",
      remittanceDays: String(courier?.remittanceDays ?? 7),
      notes: courier?.notes ?? "",
      isEnabled: courier?.isEnabled ?? true,
    },
  });

  const flammable = useWatch({ control, name: "acceptsFlammableGoods" });

  const onSubmit = handleSubmit(async (v) => {
    const body: CourierInput = {
      name: v.name.trim(),
      contactName: v.contactName.trim() || null,
      contactPhone: v.contactPhone.trim() || null,
      contactEmail: v.contactEmail.trim() || null,
      enabledCountries: v.enabledCountries,
      deliveryFee: v.deliveryFee!,
      codFeeFixed: v.codFeeFixed!,
      codFeePercent: Number(v.codFeePercent.replace(",", ".")),
      acceptsFlammableGoods: v.acceptsFlammableGoods,
      remittanceSchedule: v.remittanceSchedule as CourierInput["remittanceSchedule"],
      remittanceDays: Number(v.remittanceDays),
      notes: v.notes.trim() || null,
      isEnabled: v.isEnabled,
    };
    try {
      await save.mutateAsync({ id: courier?.id, body });
      toast.success(courier ? t.common.saved : t.couriers.created);
      onOpenChange(false);
    } catch (error) {
      applyFieldErrors(error, setError, Object.keys(courierSchema.shape));
      toast.error(describeError(error));
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      wide
      title={courier ? t.couriers.editTitle : t.couriers.new}
      footer={
        <Button type="submit" form="courier-form" disabled={formState.isSubmitting}>
          {t.common.save}
        </Button>
      }
    >
      <form id="courier-form" className="grid gap-4" onSubmit={(e) => void onSubmit(e)} noValidate>
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField label={t.couriers.name} required error={formState.errors.name?.message} {...register("name")} />
          <TextField label={t.couriers.contactName} {...register("contactName")} />
          <TextField label={t.couriers.contactPhone} type="tel" {...register("contactPhone")} />
          <TextField label={t.couriers.contactEmail} type="email" error={formState.errors.contactEmail?.message} {...register("contactEmail")} />
        </div>
        <CountryChecks register={register("enabledCountries")} />
        {formState.errors.enabledCountries ? <p className="text-xs text-destructive">{t.couriers.countriesRequired}</p> : null}
        <div className="grid gap-4 sm:grid-cols-3">
          <Controller control={control} name="deliveryFee" render={({ field, fieldState }) => <MoneyField label={t.couriers.deliveryFee} required error={fieldState.error?.message} {...field} />} />
          <Controller control={control} name="codFeeFixed" render={({ field, fieldState }) => <MoneyField label={t.couriers.codFeeFixed} required error={fieldState.error?.message} {...field} />} />
          <TextField label={t.couriers.codFeePercent} inputMode="decimal" required error={formState.errors.codFeePercent?.message} {...register("codFeePercent")} />
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <SelectField label={t.couriers.schedule} options={options(t.enums.remittance)} {...register("remittanceSchedule")} />
          <TextField label={t.couriers.remittanceDays} inputMode="numeric" required error={formState.errors.remittanceDays?.message} {...register("remittanceDays")} />
        </div>
        <label className="flex items-start gap-2 rounded-md border border-amber-300 bg-amber-50 p-3 text-sm">
          <Checkbox className="mt-0.5" {...register("acceptsFlammableGoods")} />
          <span>
            <span className="font-medium">{t.couriers.flammable}</span>
            <span className="block text-xs text-muted-foreground">{t.couriers.flammableHint}</span>
          </span>
        </label>
        {!flammable ? <p className="text-xs text-destructive">{t.couriers.notSelectable}</p> : null}
        <label className="flex items-center gap-2 text-sm">
          <Checkbox {...register("isEnabled")} /> {t.couriers.enabled}
        </label>
        <TextareaField label={t.couriers.notes} rows={3} {...register("notes")} />
      </form>
    </Dialog>
  );
}

export function CouriersPage() {
  const couriers = useCouriers();
  const { isAdmin } = useAuth();
  const [editing, setEditing] = useState<Courier | null>(null);
  const [open, setOpen] = useState(false);
  const rows = couriers.data ?? [];

  return (
    <>
      <PageHeader
        title={t.couriers.title}
        description={isAdmin ? undefined : t.couriers.adminOnly}
        actions={
          isAdmin ? (
            <Button
              onClick={() => {
                setEditing(null);
                setOpen(true);
              }}
            >
              <Plus /> {t.couriers.new}
            </Button>
          ) : null
        }
      />
      <DataTable<Courier>
        caption={t.couriers.title}
        columns={[
          { key: "name", header: t.couriers.name, cell: (c) => <span className="font-medium">{c.name}</span> },
          {
            key: "un1266",
            header: t.couriers.flammableShort,
            cell: (c) =>
              c.acceptsFlammableGoods ? (
                <Badge tone="success">
                  <Flame className="size-3" /> {t.couriers.flammableYes}
                </Badge>
              ) : (
                <Badge tone="danger">{t.couriers.flammableNo}</Badge>
              ),
          },
          { key: "selectable", header: t.couriers.selectable, cell: (c) => (c.isSelectable ? <Badge tone="success">{t.couriers.selectable}</Badge> : <Badge>{t.couriers.notSelectable}</Badge>) },
          { key: "countries", header: t.couriers.countries, cell: (c) => c.enabledCountries.map((code) => label(t.couriers.country, code)).join(", "), hideOnMobile: true },
          {
            key: "fees",
            header: t.couriers.fees,
            cell: (c) => `${formatMoney(c.deliveryFee)} · COD ${formatMoney(c.codFeeFixed)} + ${String(c.codFeePercent).replace(".", ",")}%`,
            hideOnMobile: true,
          },
          { key: "remittance", header: t.couriers.schedule, cell: (c) => `${label(t.enums.remittance, c.remittanceSchedule)} · ${c.remittanceDays} d`, hideOnMobile: true },
          {
            key: "actions",
            header: t.common.actions,
            className: "text-right",
            cell: (c) =>
              isAdmin ? (
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={`${t.common.edit} ${c.name}`}
                  onClick={() => {
                    setEditing(c);
                    setOpen(true);
                  }}
                >
                  <Pencil />
                </Button>
              ) : null,
          },
        ]}
        rows={couriers.data}
        rowKey={(c) => c.id}
        total={rows.length}
        page={1}
        pageSize={Math.max(rows.length, 1)}
        onPageChange={() => {}}
        isLoading={couriers.isLoading}
        error={couriers.error ? describeError(couriers.error) : null}
        onRetry={() => void couriers.refetch()}
      />
      {open ? <CourierDialog courier={editing} open={open} onOpenChange={setOpen} /> : null}
    </>
  );
}
