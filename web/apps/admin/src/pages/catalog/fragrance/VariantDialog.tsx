import { zodResolver } from "@hookform/resolvers/zod";
import { Button, Dialog, Input, MoneyField, SelectField, TextField, toast } from "@perfumeshop/ui";
import { ImagePlus, Trash2 } from "lucide-react";
import { useRef } from "react";
import { Controller, useFieldArray, useForm } from "react-hook-form";
import { z } from "zod";
import { useSaveVariant, useUploadImage, type CreateVariantInput, type Variant } from "@/features/catalog/api";
import { options, t } from "@/i18n";
import { apiBaseUrl } from "@/lib/api";
import { applyFieldErrors, describeError } from "@/lib/problem";

const money = z.number({ error: t.variants.priceInvalid }).positive(t.variants.priceInvalid).nullable();

const schema = z
  .object({
    sku: z.string().trim().min(1, t.variants.skuRequired).max(50, t.variants.skuRequired),
    type: z.string().min(1),
    sizeValue: z.string().refine((v) => v.trim() === "" || (/^\d+([.,]\d+)?$/.test(v.trim()) && Number(v.replace(",", ".")) > 0), t.variants.sizeInvalid),
    sizeUnit: z.string(),
    ean: z.string().refine((v) => v.trim() === "" || /^(\d{8}|\d{12,14})$/.test(v.trim()), t.variants.eanInvalid),
    price: money,
    compareAtPrice: money,
    images: z.array(z.object({ url: z.string(), altText: z.string().max(200) })),
  })
  .refine((v) => v.compareAtPrice == null || v.price == null || v.compareAtPrice > v.price, { path: ["compareAtPrice"], message: t.variants.compareAtInvalid });

type VariantForm = z.infer<typeof schema>;

/** Relative upload URLs (/media/...) are served by the API. */
export const imageSrc = (url: string) => (url.startsWith("/") ? `${apiBaseUrl}${url}` : url);

export function VariantDialog({ fragranceId, variant, open, onOpenChange }: { fragranceId: string; variant: Variant | null; open: boolean; onOpenChange: (open: boolean) => void }) {
  const save = useSaveVariant(fragranceId);
  const upload = useUploadImage();
  const fileInput = useRef<HTMLInputElement>(null);
  const isNew = variant === null;

  const { register, control, handleSubmit, formState, setError, reset } = useForm<VariantForm>({
    resolver: zodResolver(schema),
    values: {
      sku: variant?.sku ?? "",
      type: variant?.type ?? "FullBottle",
      sizeValue: variant?.size ? String(variant.size.value).replace(".", ",") : "",
      sizeUnit: variant?.size?.unit ?? "Ml",
      ean: variant?.ean ?? "",
      price: variant?.price?.amount ?? null,
      compareAtPrice: variant?.compareAtPrice?.amount ?? null,
      images: (variant?.images ?? []).map((i) => ({ url: i.url, altText: i.altText ?? "" })),
    },
  });
  const images = useFieldArray({ control, name: "images" });

  async function onFiles(files: FileList | null) {
    for (const file of Array.from(files ?? [])) {
      try {
        const uploaded = await upload.mutateAsync(file);
        images.append({ url: uploaded.url, altText: "" });
        toast.success(t.variants.imageUploaded);
      } catch (error) {
        toast.error(describeError(error));
      }
    }

    if (fileInput.current) {
      fileInput.current.value = "";
    }
  }

  const onSubmit = handleSubmit(async (v) => {
    const common = {
      sku: v.sku.trim(),
      ean: v.ean.trim() || null,
      price: v.price,
      compareAtPrice: v.compareAtPrice,
      images: v.images.map((image, index) => ({ url: image.url, altText: image.altText.trim() || null, sortOrder: index })),
    };
    try {
      if (isNew) {
        const create: CreateVariantInput = {
          ...common,
          type: v.type as CreateVariantInput["type"],
          size: v.sizeValue.trim() ? { value: Number(v.sizeValue.replace(",", ".")), unit: v.sizeUnit as "Ml" } : null,
        };
        await save.mutateAsync({ create });
      } else {
        await save.mutateAsync({ variantId: variant.id, update: common });
      }

      toast.success(t.common.saved);
      reset();
      onOpenChange(false);
    } catch (error) {
      applyFieldErrors(error, setError, ["sku", "ean", "price", "compareAtPrice", "images"]);
      toast.error(describeError(error));
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      wide
      title={isNew ? t.variants.newTitle : t.variants.editTitle}
      footer={
        <Button type="submit" form="variant-form" disabled={formState.isSubmitting || upload.isPending}>
          {formState.isSubmitting ? t.common.saving : t.common.save}
        </Button>
      }
    >
      <form id="variant-form" className="grid gap-4" onSubmit={(e) => void onSubmit(e)} noValidate>
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField label={t.variants.sku} hint={t.variants.skuHint} required error={formState.errors.sku?.message} {...register("sku")} />
          <TextField label={t.variants.ean} inputMode="numeric" error={formState.errors.ean?.message} {...register("ean")} />
        </div>
        {isNew ? (
          <div className="grid gap-4 sm:grid-cols-3">
            <SelectField label={t.variants.type} required options={options(t.enums.variantType)} {...register("type")} />
            <TextField label={t.variants.sizeValue} inputMode="decimal" error={formState.errors.sizeValue?.message} {...register("sizeValue")} />
            <SelectField label={t.variants.sizeUnit} options={options(t.enums.sizeUnit)} {...register("sizeUnit")} />
          </div>
        ) : null}
        <div className="grid gap-4 sm:grid-cols-2">
          <Controller
            control={control}
            name="price"
            render={({ field, fieldState }) => <MoneyField label={t.variants.price} error={fieldState.error?.message} {...field} />}
          />
          <Controller
            control={control}
            name="compareAtPrice"
            render={({ field, fieldState }) => <MoneyField label={t.variants.compareAt} hint={t.variants.compareAtHint} error={fieldState.error?.message} {...field} />}
          />
        </div>

        <fieldset className="grid gap-2">
          <legend className="mb-1 text-sm font-medium">{t.variants.images}</legend>
          <p className="text-xs text-muted-foreground">{t.variants.imageHint}</p>
          {images.fields.length > 0 ? (
            <ul className="grid gap-2">
              {images.fields.map((field, index) => (
                <li key={field.id} className="flex items-center gap-2">
                  <img src={imageSrc(field.url)} alt="" className="size-14 shrink-0 rounded-md border border-border object-cover" />
                  <Input aria-label={`${t.variants.imageAlt} ${index + 1}`} placeholder={t.variants.imageAlt} {...register(`images.${index}.altText`)} />
                  <Button variant="ghost" size="icon" aria-label={`${t.common.remove} ${index + 1}`} onClick={() => images.remove(index)}>
                    <Trash2 />
                  </Button>
                </li>
              ))}
            </ul>
          ) : null}
          <div>
            <input ref={fileInput} type="file" accept="image/jpeg,image/png,image/webp" multiple className="sr-only" id="variant-images" onChange={(e) => void onFiles(e.target.files)} />
            <Button variant="outline" size="sm" disabled={upload.isPending} onClick={() => fileInput.current?.click()}>
              <ImagePlus /> {upload.isPending ? t.variants.uploading : t.variants.uploadImage}
            </Button>
          </div>
        </fieldset>
      </form>
    </Dialog>
  );
}
