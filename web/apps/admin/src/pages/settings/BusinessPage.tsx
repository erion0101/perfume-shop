import { zodResolver } from "@hookform/resolvers/zod";
import { Button, Card, CardContent, ErrorState, LoadingState, TextField, toast } from "@perfumeshop/ui";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { PageHeader } from "@/components/PageHeader";
import { useBusinessProfile, useSaveBusinessProfile, type BusinessProfile } from "@/features/admin/api";
import { formatDateTime, t } from "@/i18n";
import { useAuth } from "@/lib/auth";
import { applyFieldErrors, describeError } from "@/lib/problem";

export const businessSchema = z.object({
  shopName: z.string().trim().min(1, t.settings.shopNameRequired).max(100, t.settings.shopNameRequired),
  address: z.string().trim().min(3, t.settings.addressRequired).max(200, t.settings.addressRequired),
  phone: z.string().trim().min(6, t.settings.phoneRequired).max(30, t.settings.phoneRequired),
  email: z.union([z.literal(""), z.email(t.settings.emailInvalid)]),
  taxNumber: z.string().trim().regex(/^\d{8,10}$/, t.settings.taxInvalid),
});
type Values = z.infer<typeof businessSchema>;

export function BusinessForm({ profile, canEdit }: { profile: BusinessProfile; canEdit: boolean }) {
  const save = useSaveBusinessProfile();
  const { register, handleSubmit, formState, setError } = useForm<Values>({
    resolver: zodResolver(businessSchema),
    defaultValues: { shopName: profile.shopName, address: profile.address, phone: profile.phone, email: profile.email ?? "", taxNumber: profile.taxNumber },
  });
  const placeholder = (field: string) => (profile.placeholderFields.includes(field) ? t.settings.isPlaceholder : undefined);

  const onSubmit = handleSubmit(async (values) => {
    try {
      await save.mutateAsync({ ...values, email: values.email || null });
      toast.success(t.settings.saved);
    } catch (error) {
      if (!applyFieldErrors(error, setError, ["shopName", "address", "phone", "email", "taxNumber"])) {
        toast.error(describeError(error));
      }
    }
  });

  const e = formState.errors;
  return (
    <Card>
      <CardContent>
        <form className="grid gap-4 sm:max-w-xl" onSubmit={(ev) => void onSubmit(ev)} noValidate>
          <TextField label={t.settings.shopName} error={e.shopName?.message} hint={placeholder("shopName")} disabled={!canEdit} {...register("shopName")} />
          <TextField label={t.settings.address} error={e.address?.message} hint={placeholder("address")} disabled={!canEdit} {...register("address")} />
          <TextField label={t.settings.phone} type="tel" error={e.phone?.message} hint={placeholder("phone")} disabled={!canEdit} {...register("phone")} />
          <TextField label={t.settings.email} type="email" error={e.email?.message} disabled={!canEdit} {...register("email")} />
          <TextField
            label={t.settings.taxNumber}
            inputMode="numeric"
            error={e.taxNumber?.message}
            hint={placeholder("taxNumber") ?? t.settings.taxHint}
            disabled={!canEdit}
            {...register("taxNumber")}
          />
          {profile.updatedBy ? <p className="text-xs text-muted-foreground">{t.settings.lastChange(profile.updatedBy, formatDateTime(profile.updatedAtUtc))}</p> : null}
          {canEdit ? (
            <div>
              <Button type="submit" disabled={formState.isSubmitting}>
                {t.common.save}
              </Button>
            </div>
          ) : null}
        </form>
      </CardContent>
    </Card>
  );
}

export function BusinessPage() {
  const { isAdmin } = useAuth();
  const profile = useBusinessProfile();
  return (
    <>
      <PageHeader title={t.settings.business} description={t.settings.businessIntro} />
      {profile.isLoading ? (
        <LoadingState />
      ) : profile.data ? (
        <BusinessForm key={profile.data.updatedAtUtc} profile={profile.data} canEdit={isAdmin} />
      ) : (
        <ErrorState message={describeError(profile.error)} onRetry={() => void profile.refetch()} />
      )}
    </>
  );
}
