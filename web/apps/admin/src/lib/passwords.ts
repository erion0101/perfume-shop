import { z } from "zod";
import { t } from "@/i18n";

/** Same rules as the API (ASP.NET Identity): 12+ characters with upper, lower, digit and symbol. */
export const strongPassword = z
  .string()
  .min(12, t.auth.passwordRules)
  .regex(/[A-Z]/, t.auth.passwordRules)
  .regex(/[a-z]/, t.auth.passwordRules)
  .regex(/\d/, t.auth.passwordRules)
  .regex(/[^A-Za-z0-9]/, t.auth.passwordRules);

export const newPasswordSchema = z
  .object({ newPassword: strongPassword, confirmPassword: z.string() })
  .refine((v) => v.newPassword === v.confirmPassword, { path: ["confirmPassword"], message: t.auth.passwordsDiffer });
