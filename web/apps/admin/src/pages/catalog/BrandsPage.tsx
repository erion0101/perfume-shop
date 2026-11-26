import { zodResolver } from "@hookform/resolvers/zod";
import { Button, ConfirmDialog, DataTable, Dialog, TextareaField, TextField, toast } from "@perfumeshop/ui";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { PageHeader } from "@/components/PageHeader";
import { SearchInput } from "@/components/SearchInput";
import { useBrands, useDeleteBrand, useSaveBrand, type Brand } from "@/features/catalog/api";
import { t } from "@/i18n";
import { applyFieldErrors, describeError } from "@/lib/problem";

const optionalUrl = z.union([z.literal(""), z.url(t.brands.urlInvalid)]);

const schema = z.object({
  name: z.string().trim().min(1, t.brands.nameRequired).max(100, t.brands.nameRequired),
  slug: z.string().trim().max(120),
  countryCode: z.union([z.literal(""), z.string().trim().regex(/^[A-Za-z]{2}$/, t.brands.countryInvalid)]),
  logoUrl: optionalUrl,
  description: z.string().max(4000),
});

type BrandForm = z.infer<typeof schema>;
const formFields = ["name", "slug", "countryCode", "logoUrl", "description"] as const;

function BrandDialog({ brand, open, onOpenChange }: { brand: Brand | null; open: boolean; onOpenChange: (open: boolean) => void }) {
  const save = useSaveBrand();
  const { register, handleSubmit, formState, setError, reset } = useForm<BrandForm>({
    resolver: zodResolver(schema),
    values: {
      name: brand?.name ?? "",
      slug: brand?.slug ?? "",
      countryCode: brand?.countryCode ?? "",
      logoUrl: brand?.logoUrl ?? "",
      description: brand?.description ?? "",
    },
  });

  const onSubmit = handleSubmit(async (values) => {
    try {
      await save.mutateAsync({
        id: brand?.id,
        body: {
          name: values.name,
          slug: values.slug || null,
          countryCode: values.countryCode ? values.countryCode.toUpperCase() : null,
          logoUrl: values.logoUrl || null,
          description: values.description || null,
        },
      });
      toast.success(brand ? t.common.saved : t.brands.created);
      reset();
      onOpenChange(false);
    } catch (error) {
      applyFieldErrors(error, setError, formFields);
      toast.error(describeError(error));
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={brand ? t.brands.editTitle : t.brands.new}
      footer={
        <Button type="submit" form="brand-form" disabled={formState.isSubmitting}>
          {formState.isSubmitting ? t.common.saving : t.common.save}
        </Button>
      }
    >
      <form id="brand-form" className="grid gap-4" onSubmit={(e) => void onSubmit(e)} noValidate>
        <TextField label={t.brands.name} required error={formState.errors.name?.message} {...register("name")} />
        <TextField label={t.brands.slug} hint={t.brands.slugHint} error={formState.errors.slug?.message} {...register("slug")} />
        <TextField label={t.brands.country} maxLength={2} error={formState.errors.countryCode?.message} {...register("countryCode")} />
        <TextField label={t.brands.logoUrl} type="url" error={formState.errors.logoUrl?.message} {...register("logoUrl")} />
        <TextareaField label={t.brands.description} rows={4} error={formState.errors.description?.message} {...register("description")} />
      </form>
    </Dialog>
  );
}

export function BrandsPage() {
  const [search, setSearch] = useState("");
  const brands = useBrands(search);
  const remove = useDeleteBrand();
  const [editing, setEditing] = useState<Brand | null>(null);
  const [open, setOpen] = useState(false);
  const rows = brands.data ?? [];

  return (
    <>
      <PageHeader
        title={t.brands.title}
        actions={
          <Button
            onClick={() => {
              setEditing(null);
              setOpen(true);
            }}
          >
            <Plus /> {t.brands.new}
          </Button>
        }
      />
      <DataTable<Brand>
        caption={t.brands.title}
        toolbar={<SearchInput value={search} onChange={setSearch} />}
        columns={[
          { key: "name", header: t.brands.name, cell: (b) => <span className="font-medium">{b.name}</span> },
          { key: "slug", header: t.brands.slug, cell: (b) => b.slug, hideOnMobile: true },
          { key: "country", header: t.brands.country.split(" (")[0]!, cell: (b) => b.countryCode ?? t.common.none },
          { key: "count", header: t.brands.fragrances, cell: (b) => b.fragranceCount, className: "text-right" },
          {
            key: "actions",
            header: t.common.actions,
            className: "text-right",
            cell: (b) => (
              <div className="flex justify-end gap-1">
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={`${t.common.edit} ${b.name}`}
                  onClick={() => {
                    setEditing(b);
                    setOpen(true);
                  }}
                >
                  <Pencil />
                </Button>
                <ConfirmDialog
                  trigger={
                    <Button variant="ghost" size="icon" aria-label={`${t.common.delete} ${b.name}`}>
                      <Trash2 />
                    </Button>
                  }
                  title={t.brands.deleteConfirm(b.name)}
                  description={t.brands.deleteHint}
                  confirmLabel={t.common.delete}
                  destructive
                  onConfirm={async () => {
                    try {
                      await remove.mutateAsync(b.id);
                      toast.success(t.brands.deleted);
                    } catch (error) {
                      toast.error(describeError(error));
                      throw error;
                    }
                  }}
                />
              </div>
            ),
          },
        ]}
        rows={brands.data}
        rowKey={(b) => b.id}
        total={rows.length}
        page={1}
        pageSize={Math.max(rows.length, 1)}
        onPageChange={() => {}}
        isLoading={brands.isLoading}
        error={brands.error ? describeError(brands.error) : null}
        onRetry={() => void brands.refetch()}
      />
      <BrandDialog brand={editing} open={open} onOpenChange={setOpen} />
    </>
  );
}
