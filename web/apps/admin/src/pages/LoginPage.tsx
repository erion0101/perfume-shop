import { zodResolver } from "@hookform/resolvers/zod";
import { Button, Card, CardContent, TextField } from "@perfumeshop/ui";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Navigate, useLocation, useNavigate } from "react-router";
import { z } from "zod";
import { t } from "@/i18n";
import { useAuth } from "@/lib/auth";
import { describeError } from "@/lib/problem";

const schema = z.object({
  email: z.email(t.auth.emailInvalid),
  password: z.string().min(1, t.auth.passwordRequired),
});

type LoginForm = z.infer<typeof schema>;

export function LoginPage() {
  const { session, ready, signIn } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from ?? "/";
  const [error, setError] = useState<string | null>(null);
  const { register, handleSubmit, formState } = useForm<LoginForm>({ resolver: zodResolver(schema), defaultValues: { email: "", password: "" } });

  if (ready && session) {
    return <Navigate to={from} replace />;
  }

  const onSubmit = handleSubmit(async ({ email, password }) => {
    setError(null);
    try {
      await signIn(email, password);
      navigate(from, { replace: true });
    } catch (e) {
      setError(describeError(e));
    }
  });

  return (
    <main className="flex min-h-dvh items-center justify-center bg-muted/40 p-4 font-sans text-foreground">
      <Card className="w-full max-w-sm">
        <CardContent className="grid gap-5 p-6">
          <div className="grid gap-1">
            <p className="text-sm font-semibold text-muted-foreground">PerfumeShop</p>
            <h1 className="text-xl font-semibold">{t.auth.title}</h1>
          </div>
          <form className="grid gap-4" onSubmit={(e) => void onSubmit(e)} noValidate>
            <TextField label={t.auth.email} type="email" autoComplete="username" required error={formState.errors.email?.message} {...register("email")} />
            <TextField
              label={t.auth.password}
              type="password"
              autoComplete="current-password"
              required
              error={formState.errors.password?.message}
              {...register("password")}
            />
            {error ? (
              <p role="alert" className="rounded-md bg-red-50 p-3 text-sm text-destructive">
                {error}
              </p>
            ) : null}
            <Button type="submit" disabled={formState.isSubmitting}>
              {formState.isSubmitting ? t.auth.signingIn : t.auth.signIn}
            </Button>
          </form>
        </CardContent>
      </Card>
    </main>
  );
}
