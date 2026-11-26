import { zodResolver } from "@hookform/resolvers/zod";
import { Badge, Button, ConfirmDialog, DataTable, Dialog, SelectField, TextField, toast } from "@perfumeshop/ui";
import { KeyRound, Plus, Wand2 } from "lucide-react";
import { useState } from "react";
import { useForm, type UseFormRegisterReturn } from "react-hook-form";
import { z } from "zod";
import { PageHeader } from "@/components/PageHeader";
import { generatePassword, useCreateStaff, useResetStaffPassword, useSetStaffActive, useStaff, type StaffAccount } from "@/features/staff/api";
import { formatDateTime, t } from "@/i18n";
import { useAuth } from "@/lib/auth";
import { applyFieldErrors, describeError } from "@/lib/problem";
import { strongPassword as passwordRule } from "@/lib/passwords";

const createSchema = z.object({
  displayName: z.string().trim().min(1, t.staff.nameRequired).max(100, t.staff.nameRequired),
  email: z.email(t.staff.emailInvalid),
  role: z.enum(["Admin", "Staff"]),
  temporaryPassword: passwordRule,
});

function PasswordWithGenerator({ register, error, onGenerate }: { register: UseFormRegisterReturn; error?: string; onGenerate: () => void }) {
  return (
    <div className="grid gap-2">
      <TextField label={t.staff.temporaryPassword} autoComplete="off" required hint={t.staff.tempHint} error={error} {...register} />
      <div className="flex gap-2">
        <Button variant="outline" size="sm" onClick={onGenerate}>
          <Wand2 /> {t.staff.generate}
        </Button>
      </div>
    </div>
  );
}

function CreateStaffDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const create = useCreateStaff();
  const { register, handleSubmit, formState, setValue, setError, reset } = useForm<z.infer<typeof createSchema>>({
    resolver: zodResolver(createSchema),
    defaultValues: { displayName: "", email: "", role: "Staff", temporaryPassword: "" },
  });
  const onSubmit = handleSubmit(async (v) => {
    try {
      await create.mutateAsync({ displayName: v.displayName.trim(), email: v.email.trim().toLowerCase(), role: v.role, temporaryPassword: v.temporaryPassword });
      await navigator.clipboard?.writeText(v.temporaryPassword).catch(() => undefined);
      toast.success(t.staff.created);
      reset();
      onOpenChange(false);
    } catch (error) {
      applyFieldErrors(error, setError, ["displayName", "email", "role", "temporaryPassword"]);
      toast.error(describeError(error));
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={t.staff.new}
      footer={
        <Button type="submit" form="staff-form" disabled={formState.isSubmitting}>
          {t.common.create}
        </Button>
      }
    >
      <form id="staff-form" className="grid gap-4" onSubmit={(e) => void onSubmit(e)} noValidate>
        <TextField label={t.staff.name} required error={formState.errors.displayName?.message} {...register("displayName")} />
        <TextField label={t.staff.email} type="email" required error={formState.errors.email?.message} {...register("email")} />
        <SelectField label={t.staff.role} options={[{ value: "Staff", label: t.auth.roleStaff }, { value: "Admin", label: t.auth.roleAdmin }]} {...register("role")} />
        <PasswordWithGenerator
          register={register("temporaryPassword")}
          error={formState.errors.temporaryPassword?.message}
          onGenerate={() => setValue("temporaryPassword", generatePassword(), { shouldValidate: true })}
        />
      </form>
    </Dialog>
  );
}

function ResetPasswordDialog({ staff, onClose }: { staff: StaffAccount; onClose: () => void }) {
  const reset = useResetStaffPassword();
  const { register, handleSubmit, formState, setValue } = useForm({ resolver: zodResolver(z.object({ temporaryPassword: passwordRule })), defaultValues: { temporaryPassword: "" } });
  const onSubmit = handleSubmit(async ({ temporaryPassword }) => {
    try {
      await reset.mutateAsync({ id: staff.id, temporaryPassword });
      toast.success(t.staff.reset);
      onClose();
    } catch (error) {
      toast.error(describeError(error));
    }
  });

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title={t.staff.resetTitle(staff.email)}
      footer={
        <Button type="submit" form="reset-form" disabled={formState.isSubmitting}>
          {t.staff.resetPassword}
        </Button>
      }
    >
      <form id="reset-form" onSubmit={(e) => void onSubmit(e)} noValidate>
        <PasswordWithGenerator register={register("temporaryPassword")} error={formState.errors.temporaryPassword?.message} onGenerate={() => setValue("temporaryPassword", generatePassword(), { shouldValidate: true })} />
      </form>
    </Dialog>
  );
}

export function StaffPage() {
  const { session } = useAuth();
  const staff = useStaff();
  const setActive = useSetStaffActive();
  const [creating, setCreating] = useState(false);
  const [resetting, setResetting] = useState<StaffAccount | null>(null);
  const rows = staff.data ?? [];

  async function toggle(s: StaffAccount) {
    try {
      await setActive.mutateAsync({ id: s.id, active: !s.isActive });
      toast.success(s.isActive ? t.staff.deactivated : t.staff.reactivated);
    } catch (error) {
      toast.error(describeError(error));
      throw error;
    }
  }

  return (
    <>
      <PageHeader
        title={t.staff.title}
        description={t.staff.intro}
        actions={
          <Button onClick={() => setCreating(true)}>
            <Plus /> {t.staff.new}
          </Button>
        }
      />
      <DataTable<StaffAccount>
        caption={t.staff.title}
        columns={[
          {
            key: "name",
            header: t.staff.name,
            cell: (s) => (
              <span className="grid">
                <span className="font-medium">
                  {s.displayName ?? s.email}
                  {s.id === session?.userId ? ` (${t.staff.you})` : ""}
                </span>
                <span className="text-xs text-muted-foreground">{s.email}</span>
              </span>
            ),
          },
          { key: "role", header: t.staff.role, cell: (s) => (s.role === "Admin" ? t.auth.roleAdmin : t.auth.roleStaff) },
          {
            key: "status",
            header: t.staff.status,
            cell: (s) => (
              <span className="flex flex-wrap gap-1">
                <Badge tone={s.isActive ? "success" : "neutral"}>{s.isActive ? t.staff.active : t.staff.inactive}</Badge>
                {s.mustChangePassword ? <Badge tone="warning">{t.staff.mustChange}</Badge> : null}
                {s.lockedOut ? <Badge tone="danger">{t.staff.lockedOut}</Badge> : null}
              </span>
            ),
          },
          { key: "2fa", header: t.staff.twoFactor, cell: (s) => <Badge tone={s.twoFactorEnabled ? "success" : "neutral"}>{s.twoFactorEnabled ? t.staff.twoFactorOn : t.staff.twoFactorOff}</Badge> },
          { key: "login", header: t.staff.lastLogin, cell: (s) => (s.lastLoginAtUtc ? formatDateTime(s.lastLoginAtUtc) : t.staff.never), hideOnMobile: true },
          {
            key: "actions",
            header: t.common.actions,
            className: "text-right",
            cell: (s) => (
              <span className="flex flex-wrap justify-end gap-1">
                <Button variant="ghost" size="sm" onClick={() => setResetting(s)} aria-label={`${t.staff.resetPassword} ${s.email}`}>
                  <KeyRound /> {t.staff.resetPassword}
                </Button>
                {s.id !== session?.userId ? (
                  s.isActive ? (
                    <ConfirmDialog
                      trigger={
                        <Button variant="outline" size="sm" aria-label={`${t.staff.deactivate} ${s.email}`}>
                          {t.staff.deactivate}
                        </Button>
                      }
                      title={t.staff.deactivateTitle(s.email)}
                      description={t.staff.deactivateHint}
                      confirmLabel={t.staff.deactivate}
                      destructive
                      onConfirm={() => toggle(s)}
                    />
                  ) : (
                    <Button variant="outline" size="sm" onClick={() => void toggle(s).catch(() => undefined)} aria-label={`${t.staff.reactivate} ${s.email}`}>
                      {t.staff.reactivate}
                    </Button>
                  )
                ) : null}
              </span>
            ),
          },
        ]}
        rows={staff.data}
        rowKey={(s) => s.id}
        total={rows.length}
        page={1}
        pageSize={Math.max(rows.length, 1)}
        onPageChange={() => {}}
        isLoading={staff.isLoading}
        error={staff.error ? describeError(staff.error) : null}
        onRetry={() => void staff.refetch()}
      />
      <CreateStaffDialog open={creating} onOpenChange={setCreating} />
      {resetting ? <ResetPasswordDialog staff={resetting} onClose={() => setResetting(null)} /> : null}
    </>
  );
}
