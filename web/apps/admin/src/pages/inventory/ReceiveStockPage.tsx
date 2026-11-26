import { zodResolver } from "@hookform/resolvers/zod";
import { Button, Card, CardContent, CardHeader, CardTitle, MoneyField, TextField, toast } from "@perfumeshop/ui";
import { Plus, Trash2 } from "lucide-react";
import { Controller, useFieldArray, useForm } from "react-hook-form";
import { useNavigate, useSearchParams } from "react-router";
import { z } from "zod";
import { PageHeader } from "@/components/PageHeader";
import { VariantPicker } from "@/components/VariantPicker";
import { useReceiveStock } from "@/features/inventory/api";
import { t } from "@/i18n";
import { applyFieldErrors, describeError } from "@/lib/problem";

const today = () => new Date().toLocaleDateString("sv-SE", { timeZone: "Europe/Belgrade" }); // yyyy-MM-dd, Kosovo time

const line = z.object({
  variantId: z.string().min(1, t.receive.variantRequired),
  batchCode: z.string().trim().min(1, t.receive.batchRequired).max(50, t.receive.batchRequired),
  quantity: z.string().regex(/^\d+$/, t.receive.quantityInvalid).refine((v) => Number(v) >= 1 && Number(v) <= 10_000, t.receive.quantityInvalid),
  unitCost: z.number({ error: t.receive.costInvalid }).min(0, t.receive.costInvalid).nullable().refine((v) => v !== null, t.receive.costInvalid),
});

export const receiveSchema = z
  .object({
    supplierName: z.string().trim().min(1, t.receive.supplierRequired).max(200),
    supplierInvoiceNumber: z.string().trim().min(1, t.receive.invoiceRequired).max(100),
    supplierInvoiceDate: z.string().min(1, t.receive.dateRequired),
    receivedOn: z.string().min(1, t.receive.dateRequired),
    lines: z.array(line).min(1, t.receive.linesRequired),
  })
  .refine((v) => !v.supplierInvoiceDate || !v.receivedOn || v.receivedOn >= v.supplierInvoiceDate, { path: ["receivedOn"], message: t.receive.dateOrder });

export type ReceiveForm = z.input<typeof receiveSchema>;
type ReceiveValues = z.output<typeof receiveSchema>;

const emptyLine = (variantId = ""): ReceiveForm["lines"][number] => ({ variantId, batchCode: "", quantity: "1", unitCost: null });

export function ReceiveStockPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const receive = useReceiveStock();
  // From a fragrance page: "Prano mallin" for one variant (?fragranceId=…&variantId=…).
  const preselectedFragrance = params.get("fragranceId") ?? "";
  const { register, control, handleSubmit, formState, setError } = useForm<ReceiveForm, unknown, ReceiveValues>({
    resolver: zodResolver(receiveSchema),
    defaultValues: { supplierName: "", supplierInvoiceNumber: "", supplierInvoiceDate: today(), receivedOn: today(), lines: [emptyLine(params.get("variantId") ?? "")] },
  });
  const lines = useFieldArray({ control, name: "lines" });

  const onSubmit = handleSubmit(async (v) => {
    try {
      const result = await receive.mutateAsync({
        supplierName: v.supplierName.trim(),
        supplierInvoiceNumber: v.supplierInvoiceNumber.trim(),
        supplierInvoiceDate: v.supplierInvoiceDate,
        receivedOn: v.receivedOn,
        lines: v.lines.map((l) => ({ variantId: l.variantId, batchCode: l.batchCode.trim(), quantity: Number(l.quantity), unitCost: l.unitCost ?? 0 })),
      });
      toast.success(t.receive.received(result.batches.length));
      navigate(v.lines.length === 1 ? `/inventory/items/${v.lines[0]!.variantId}` : "/inventory");
    } catch (error) {
      applyFieldErrors(error, setError, ["supplierName", "supplierInvoiceNumber", "supplierInvoiceDate", "receivedOn", "lines"]);
      toast.error(describeError(error));
    }
  });

  return (
    <>
      <PageHeader title={t.receive.title} description={t.receive.intro} />
      <form className="grid gap-4" onSubmit={(e) => void onSubmit(e)} noValidate aria-label={t.receive.title}>
        <Card>
          <CardContent className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <TextField label={t.receive.supplier} required error={formState.errors.supplierName?.message} {...register("supplierName")} />
            <TextField label={t.receive.invoiceNumber} required error={formState.errors.supplierInvoiceNumber?.message} {...register("supplierInvoiceNumber")} />
            <TextField label={t.receive.invoiceDate} type="date" required error={formState.errors.supplierInvoiceDate?.message} {...register("supplierInvoiceDate")} />
            <TextField label={t.receive.receivedOn} type="date" required error={formState.errors.receivedOn?.message} {...register("receivedOn")} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>{t.receive.lines}</CardTitle>
            <Button variant="outline" size="sm" onClick={() => lines.append(emptyLine())}>
              <Plus /> {t.receive.addLine}
            </Button>
          </CardHeader>
          <CardContent className="grid gap-4">
            {lines.fields.map((field, index) => {
              const errors = formState.errors.lines?.[index];
              return (
                <fieldset key={field.id} className="grid gap-3 rounded-md border border-border p-3" aria-label={`${t.receive.lines} ${index + 1}`}>
                  <Controller
                    control={control}
                    name={`lines.${index}.variantId`}
                    render={({ field: f }) => (
                      <VariantPicker
                        index={index}
                        value={f.value}
                        onChange={f.onChange}
                        error={errors?.variantId?.message}
                        initialFragranceId={index === 0 ? preselectedFragrance : ""}
                      />
                    )}
                  />
                  <div className="grid gap-3 sm:grid-cols-[1fr_8rem_12rem_auto] sm:items-end">
                    <TextField label={t.receive.batchCode} required error={errors?.batchCode?.message} {...register(`lines.${index}.batchCode`)} />
                    <TextField label={t.receive.quantity} inputMode="numeric" required error={errors?.quantity?.message} {...register(`lines.${index}.quantity`)} />
                    <Controller
                      control={control}
                      name={`lines.${index}.unitCost`}
                      render={({ field: f, fieldState }) => <MoneyField label={t.receive.unitCost} required error={fieldState.error?.message} {...f} />}
                    />
                    <Button variant="ghost" size="icon" aria-label={`${t.receive.removeLine} ${index + 1}`} disabled={lines.fields.length === 1} onClick={() => lines.remove(index)}>
                      <Trash2 />
                    </Button>
                  </div>
                </fieldset>
              );
            })}
            {formState.errors.lines?.root?.message || formState.errors.lines?.message ? (
              <p role="alert" className="text-sm text-destructive">
                {formState.errors.lines.root?.message ?? formState.errors.lines.message}
              </p>
            ) : null}
          </CardContent>
        </Card>

        <div className="flex justify-end">
          <Button type="submit" size="lg" disabled={formState.isSubmitting}>
            {formState.isSubmitting ? t.common.saving : t.receive.submit}
          </Button>
        </div>
      </form>
    </>
  );
}
