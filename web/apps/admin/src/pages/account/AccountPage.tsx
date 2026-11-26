import { zodResolver } from "@hookform/resolvers/zod";
import { Badge, Button, Card, CardContent, CardHeader, CardTitle, Dialog, ErrorState, LoadingState, TextField, toast } from "@perfumeshop/ui";
import { KeyRound, ShieldCheck } from "lucide-react";
import QRCode from "qrcode";
import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { PageHeader } from "@/components/PageHeader";
import { useChangePassword, useMyAccount, useSetupTwoFactor, useTwoFactorChange } from "@/features/staff/api";
import { formatDateTime, t } from "@/i18n";
import { useAuth } from "@/lib/auth";
import { describeError } from "@/lib/problem";
import { newPasswordSchema } from "@/lib/passwords";

const changeSchema = z.object({ currentPassword: z.string().min(1, t.auth.passwordRequired) }).and(newPasswordSchema);

function PasswordCard() {
  const change = useChangePassword();
  const { register, handleSubmit, formState, reset } = useForm({ resolver: zodResolver(changeSchema), defaultValues: { currentPassword: "", newPassword: "", confirmPassword: "" } });
  const onSubmit = handleSubmit(async (v) => {
    try {
      await change.mutateAsync({ currentPassword: v.currentPassword, newPassword: v.newPassword });
      toast.success(t.account.changed);
      reset();
    } catch (error) {
      toast.error(describeError(error));
    }
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t.account.password}</CardTitle>
      </CardHeader>
      <CardContent>
        <form className="grid gap-4 sm:max-w-sm" onSubmit={(e) => void onSubmit(e)} noValidate aria-label={t.account.password}>
          <TextField label={t.account.currentPassword} type="password" autoComplete="current-password" required error={formState.errors.currentPassword?.message} {...register("currentPassword")} />
          <TextField label={t.account.newPassword} type="password" autoComplete="new-password" required hint={t.auth.passwordRules} error={formState.errors.newPassword?.message} {...register("newPassword")} />
          <TextField label={t.account.confirmPassword} type="password" autoComplete="new-password" required error={formState.errors.confirmPassword?.message} {...register("confirmPassword")} />
          <div>
            <Button type="submit" disabled={formState.isSubmitting}>
              <KeyRound /> {t.account.change}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

function RecoveryCodesDialog({ codes, onClose }: { codes: string[]; onClose: () => void }) {
  const text = codes.join("\n");
  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title={t.account.recoveryTitle}
      description={t.account.recoveryHint}
      footer={
        <>
          <Button
            variant="outline"
            onClick={() => {
              const link = document.createElement("a");
              link.href = URL.createObjectURL(new Blob([`${t.appName} – ${t.account.recoveryTitle}\n\n${text}\n`], { type: "text/plain" }));
              link.download = "perfumeshop-recovery-codes.txt";
              link.click();
            }}
          >
            {t.account.download}
          </Button>
          <Button onClick={onClose}>{t.account.done}</Button>
        </>
      }
    >
      <ul className="grid grid-cols-2 gap-2 rounded-md bg-muted p-3 font-mono text-sm" aria-label={t.account.recoveryTitle}>
        {codes.map((code) => (
          <li key={code}>{code}</li>
        ))}
      </ul>
    </Dialog>
  );
}

function TwoFactorCard() {
  const me = useMyAccount();
  const setup = useSetupTwoFactor();
  const enable = useTwoFactorChange("enable");
  const disable = useTwoFactorChange("disable");
  const renew = useTwoFactorChange("recovery-codes");
  const [qr, setQr] = useState<string | null>(null);
  const [code, setCode] = useState("");
  const [codes, setCodes] = useState<string[] | null>(null);

  useEffect(() => {
    if (setup.data) {
      void QRCode.toString(setup.data.authenticatorUri, { type: "svg", margin: 1, width: 192 }).then(setQr);
    }
  }, [setup.data]);

  if (me.isLoading) {
    return <LoadingState />;
  }

  if (!me.data) {
    return <ErrorState message={describeError(me.error)} onRetry={() => void me.refetch()} />;
  }

  const m = me.data;
  async function run(action: typeof enable, done: string) {
    try {
      const result = await action.mutateAsync(code.trim());
      setCode("");
      toast.success(done);
      if (result && "recoveryCodes" in result) {
        setCodes(result.recoveryCodes);
      }

      setup.reset();
      setQr(null);
    } catch (error) {
      toast.error(describeError(error));
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t.account.twoFactor}</CardTitle>
        <span className="flex gap-2">
          <Badge tone={m.twoFactorEnabled ? "success" : "warning"}>{m.twoFactorEnabled ? t.account.twoFactorOn : t.account.twoFactorOff}</Badge>
          {m.twoFactorRequired ? <Badge tone="info">{t.account.required}</Badge> : null}
        </span>
      </CardHeader>
      <CardContent className="grid gap-4 text-sm">
        <p className="text-muted-foreground">{t.account.twoFactorIntro}</p>
        {!m.twoFactorEnabled && !setup.data ? (
          <div>
            <Button onClick={() => void setup.mutateAsync().catch((error: unknown) => toast.error(describeError(error)))} disabled={setup.isPending}>
              <ShieldCheck /> {t.account.enable}
            </Button>
          </div>
        ) : null}

        {!m.twoFactorEnabled && setup.data ? (
          <div className="grid gap-4 sm:max-w-md">
            <p>{t.account.scan}</p>
            {qr ? <img src={`data:image/svg+xml;charset=utf-8,${encodeURIComponent(qr)}`} alt={t.account.scan} className="size-48 rounded-md border border-border bg-white p-2" /> : null}
            <p>
              {t.account.key}: <code className="select-all rounded bg-muted px-1.5 py-0.5 font-mono" data-testid="authenticator-key">{setup.data.sharedKey}</code>
            </p>
            <p>{t.account.enterCode}</p>
            <div className="flex gap-2">
              <TextField label={t.auth.twoFactorCode} inputMode="numeric" autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value)} className="w-40" />
              <Button className="self-end" disabled={code.trim().length < 6 || enable.isPending} onClick={() => void run(enable, t.account.enabled)}>
                {t.account.confirm}
              </Button>
            </div>
          </div>
        ) : null}

        {m.twoFactorEnabled ? (
          <div className="grid gap-3 sm:max-w-md">
            <p className="text-muted-foreground">{t.account.recoveryLeft(m.recoveryCodesLeft)}</p>
            <TextField label={t.account.codeForAction} inputMode="numeric" autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value)} className="w-48" />
            <div className="flex flex-wrap gap-2">
              <Button variant="outline" disabled={code.trim().length < 6 || renew.isPending} onClick={() => void run(renew, t.account.newCodes)}>
                {t.account.newCodes}
              </Button>
              {!m.twoFactorRequired ? (
                <Button variant="destructive" disabled={code.trim().length < 6 || disable.isPending} onClick={() => void run(disable, t.account.disabled)}>
                  {t.account.disable}
                </Button>
              ) : null}
            </div>
          </div>
        ) : null}
      </CardContent>
      {codes ? <RecoveryCodesDialog codes={codes} onClose={() => setCodes(null)} /> : null}
    </Card>
  );
}

export function AccountPage() {
  const { session } = useAuth();
  const me = useMyAccount();
  return (
    <>
      <PageHeader
        title={t.account.title}
        description={`${session?.displayName ?? ""} ${session?.email ?? ""} · ${t.account.roles}: ${(session?.roles ?? []).join(", ")}${me.data?.lastLoginAtUtc ? ` · ${t.staff.lastLogin}: ${formatDateTime(me.data.lastLoginAtUtc)}` : ""}`}
      />
      {session?.twoFactorSetupRequired ? (
        <p role="alert" className="mb-4 rounded-md border border-amber-300 bg-amber-50 p-3 text-sm">
          {t.auth.setupRequired}
        </p>
      ) : null}
      <div className="grid gap-4 lg:grid-cols-2">
        <TwoFactorCard />
        <PasswordCard />
      </div>
    </>
  );
}
