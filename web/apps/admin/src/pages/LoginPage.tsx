import { zodResolver } from "@hookform/resolvers/zod";
import { Button, Card, CardContent, TextField } from "@perfumeshop/ui";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Navigate, useLocation, useNavigate } from "react-router";
import { z } from "zod";
import { setInitialPassword } from "@/features/staff/api";
import { t } from "@/i18n";
import { useAuth } from "@/lib/auth";
import { newPasswordSchema } from "@/lib/passwords";
import { ApiError, describeError } from "@/lib/problem";

const credentialsSchema = z.object({
  email: z.email(t.auth.emailInvalid),
  password: z.string().min(1, t.auth.passwordRequired),
});

type Credentials = z.infer<typeof credentialsSchema>;
type Step = { kind: "credentials" } | { kind: "twoFactor"; recovery: boolean } | { kind: "newPassword" };

function Frame({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <main className="flex min-h-dvh items-center justify-center bg-muted/40 p-4 font-sans text-foreground">
      <Card className="w-full max-w-sm">
        <CardContent className="grid gap-5 p-6">
          <div className="grid gap-1">
            <p className="text-sm font-semibold text-muted-foreground">PerfumeShop</p>
            <h1 className="text-xl font-semibold">{title}</h1>
          </div>
          {children}
        </CardContent>
      </Card>
    </main>
  );
}

function ErrorBox({ message }: { message: string | null }) {
  return message ? (
    <p role="alert" className="rounded-md bg-red-50 p-3 text-sm text-destructive">
      {message}
    </p>
  ) : null;
}

/** Sign-in in up to three steps: email + password → (2FA code) or (new password after a temporary one). */
export function LoginPage() {
  const { session, ready, signIn } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from ?? "/";
  const [step, setStep] = useState<Step>({ kind: "credentials" });
  const [credentials, setCredentials] = useState<Credentials | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const form = useForm<Credentials>({ resolver: zodResolver(credentialsSchema), defaultValues: { email: "", password: "" } });
  const passwordForm = useForm({ resolver: zodResolver(newPasswordSchema), defaultValues: { newPassword: "", confirmPassword: "" } });

  if (ready && session) {
    return <Navigate to={session.twoFactorSetupRequired ? "/account" : from} replace />;
  }

  async function attempt(values: Credentials, second?: { twoFactorCode?: string; recoveryCode?: string }) {
    setError(null);
    try {
      await signIn(values.email, values.password, second);
      navigate(from, { replace: true });
    } catch (e) {
      if (e instanceof ApiError && e.code === "Customers.Auth.TwoFactorRequired") {
        setStep({ kind: "twoFactor", recovery: false });
      } else if (e instanceof ApiError && e.code === "Customers.Auth.PasswordChangeRequired") {
        setStep({ kind: "newPassword" });
      } else {
        setError(describeError(e));
      }
    }
  }

  if (step.kind === "twoFactor" && credentials) {
    return (
      <Frame title={t.auth.twoFactorTitle}>
        <form
          className="grid gap-4"
          noValidate
          onSubmit={(e) => {
            e.preventDefault();
            if (!code.trim()) {
              setError(t.auth.codeRequired);
              return;
            }

            setBusy(true);
            void attempt(credentials, step.recovery ? { recoveryCode: code.trim() } : { twoFactorCode: code.trim() }).finally(() => setBusy(false));
          }}
        >
          <p className="text-sm text-muted-foreground">{t.auth.twoFactorHint}</p>
          <TextField
            label={step.recovery ? t.auth.recoveryCode : t.auth.twoFactorCode}
            autoComplete="one-time-code"
            inputMode={step.recovery ? "text" : "numeric"}
            autoFocus
            value={code}
            onChange={(e) => setCode(e.target.value)}
          />
          <ErrorBox message={error} />
          <Button type="submit" disabled={busy}>
            {busy ? t.auth.signingIn : t.auth.signIn}
          </Button>
          <Button
            variant="link"
            onClick={() => {
              setCode("");
              setError(null);
              setStep({ kind: "twoFactor", recovery: !step.recovery });
            }}
          >
            {step.recovery ? t.auth.useAuthenticator : t.auth.useRecoveryCode}
          </Button>
        </form>
      </Frame>
    );
  }

  if (step.kind === "newPassword" && credentials) {
    const onSubmit = passwordForm.handleSubmit(async ({ newPassword }) => {
      setError(null);
      try {
        await setInitialPassword({ email: credentials.email, temporaryPassword: credentials.password, newPassword, twoFactorCode: null });
        const updated = { ...credentials, password: newPassword };
        setCredentials(updated);
        await attempt(updated);
      } catch (e) {
        setError(describeError(e));
      }
    });
    return (
      <Frame title={t.auth.firstLoginTitle}>
        <form className="grid gap-4" noValidate onSubmit={(e) => void onSubmit(e)}>
          <p className="text-sm text-muted-foreground">{t.auth.firstLoginHint}</p>
          <TextField label={t.auth.newPassword} type="password" autoComplete="new-password" required error={passwordForm.formState.errors.newPassword?.message} {...passwordForm.register("newPassword")} />
          <TextField label={t.auth.confirmPassword} type="password" autoComplete="new-password" required error={passwordForm.formState.errors.confirmPassword?.message} {...passwordForm.register("confirmPassword")} />
          <ErrorBox message={error} />
          <Button type="submit" disabled={passwordForm.formState.isSubmitting}>
            {t.auth.savePassword}
          </Button>
        </form>
      </Frame>
    );
  }

  const onSubmit = form.handleSubmit(async (values) => {
    setCredentials(values);
    await attempt(values);
  });

  return (
    <Frame title={t.auth.title}>
      <form className="grid gap-4" onSubmit={(e) => void onSubmit(e)} noValidate>
        <TextField label={t.auth.email} type="email" autoComplete="username" required error={form.formState.errors.email?.message} {...form.register("email")} />
        <TextField label={t.auth.password} type="password" autoComplete="current-password" required error={form.formState.errors.password?.message} {...form.register("password")} />
        <ErrorBox message={error} />
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {form.formState.isSubmitting ? t.auth.signingIn : t.auth.signIn}
        </Button>
      </form>
    </Frame>
  );
}
