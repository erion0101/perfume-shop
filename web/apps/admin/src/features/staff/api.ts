import type { Schema } from "@perfumeshop/api-client";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, refreshSession, unwrap } from "@/lib/api";

export type StaffAccount = Schema<"StaffAccountDto">;

export function useStaff() {
  return useQuery({ queryKey: ["staff", "list"], queryFn: () => unwrap(api.GET("/api/admin/staff")) });
}

function useStaffMutation<T>(fn: (input: T) => Promise<unknown>) {
  const queryClient = useQueryClient();
  return useMutation({ mutationFn: fn, onSuccess: () => void queryClient.invalidateQueries({ queryKey: ["staff"] }) });
}

export const useCreateStaff = () => useStaffMutation((body: Schema<"CreateStaffRequest">) => unwrap(api.POST("/api/admin/staff", { body })));

export const useSetStaffActive = () =>
  useStaffMutation(({ id, active }: { id: string; active: boolean }) =>
    active
      ? unwrap(api.POST("/api/admin/staff/{id}/reactivate", { params: { path: { id } } }))
      : unwrap(api.POST("/api/admin/staff/{id}/deactivate", { params: { path: { id } } })),
  );

export const useResetStaffPassword = () =>
  useStaffMutation(({ id, temporaryPassword }: { id: string; temporaryPassword: string }) =>
    unwrap(api.POST("/api/admin/staff/{id}/reset-password", { params: { path: { id } }, body: { temporaryPassword } })),
  );

export function useMyAccount() {
  return useQuery({ queryKey: ["staff", "me"], queryFn: () => unwrap(api.GET("/api/auth/staff/me")) });
}

export const useChangePassword = () =>
  useStaffMutation((body: Schema<"ChangePasswordRequest">) => unwrap(api.POST("/api/auth/staff/me/password", { body })));

export const useSetupTwoFactor = () => useMutation({ mutationFn: () => unwrap(api.POST("/api/auth/staff/me/2fa/setup")) });

/** After enabling/disabling 2FA the session is refreshed, so the new token carries (or drops) the "mfa" claim. */
export function useTwoFactorChange(kind: "enable" | "disable" | "recovery-codes") {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (code: string) => {
      const body = { code };
      const result =
        kind === "enable"
          ? await unwrap(api.POST("/api/auth/staff/me/2fa/enable", { body }))
          : kind === "disable"
            ? await unwrap(api.POST("/api/auth/staff/me/2fa/disable", { body })).then(() => null)
            : await unwrap(api.POST("/api/auth/staff/me/2fa/recovery-codes", { body }));
      if (kind !== "recovery-codes") {
        await refreshSession();
      }

      return result;
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ["staff"] }),
  });
}

/** First sign-in / after a reset: temporary password → own password (no session yet). */
export function setInitialPassword(body: Schema<"InitialPasswordRequest">) {
  return unwrap(api.POST("/api/auth/staff/password/initial", { body }));
}

/** A strong random temporary password (meets the 12+ chars, upper, lower, digit, symbol rules). */
export function generatePassword(length = 16) {
  const sets = ["ABCDEFGHJKLMNPQRSTUVWXYZ", "abcdefghijkmnopqrstuvwxyz", "23456789", "!@#$%*-_+="];
  const all = sets.join("");
  const random = (max: number) => crypto.getRandomValues(new Uint32Array(1))[0]! % max;
  const chars = sets.map((set) => set[random(set.length)]!);
  while (chars.length < length) {
    chars.push(all[random(all.length)]!);
  }

  for (let i = chars.length - 1; i > 0; i--) {
    const j = random(i + 1);
    [chars[i], chars[j]] = [chars[j]!, chars[i]!];
  }

  return chars.join("");
}
