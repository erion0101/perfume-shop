import { zodResolver } from "@hookform/resolvers/zod";
import { Button, Card, CardContent, CardHeader, CardTitle, Checkbox, ErrorState, LoadingState, moneyToInput, parseMoney, TextField, toast } from "@perfumeshop/ui";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { PageHeader } from "@/components/PageHeader";
import { useDeliveryRates, useOrderSettings, useSaveDeliveryRate, useSaveOrderSettings, type DeliveryRate } from "@/features/settings/api";
import { t } from "@/i18n";
import { applyFieldErrors, describeError } from "@/lib/problem";

export function RateRow({ rate }: { rate: DeliveryRate }) {
  const save = useSaveDeliveryRate();
  const [enabled, setEnabled] = useState(rate.isEnabled);
  const [fee, setFee] = useState(moneyToInput(rate.fee));
  const [free, setFree] = useState(moneyToInput(rate.freeDeliveryFrom));
  const feeValue = parseMoney(fee);
  const freeValue = parseMoney(free);
  const feeError = feeValue === null || Number.isNaN(feeValue) || feeValue > 100 ? t.settings.feeInvalid : undefined;
  const freeError = freeValue !== null && (Number.isNaN(freeValue) || freeValue <= 0) ? t.settings.freeInvalid : undefined;

  async function submit() {
    try {
      await save.mutateAsync({ countryCode: rate.countryCode, body: { isEnabled: enabled, fee: feeValue!, freeDeliveryFrom: freeValue } });
      toast.success(t.settings.rateSaved(rate.countryName));
    } catch (error) {
      toast.error(describeError(error));
    }
  }

  return (
    <fieldset className="grid items-start gap-3 border-b border-border pb-4 last:border-0 last:pb-0 sm:grid-cols-[10rem_7rem_9rem_auto]" aria-label={rate.countryName}>
      <legend className="sr-only">{rate.countryName}</legend>
      <label className="flex items-center gap-2 pt-7 text-sm font-medium">
        <Checkbox checked={enabled} onChange={(e) => setEnabled(e.target.checked)} aria-label={`${t.settings.enabledCountry}: ${rate.countryName}`} />
        {rate.countryName} <span className="text-xs text-muted-foreground">{rate.countryCode}</span>
      </label>
      <TextField label={t.settings.fee} inputMode="decimal" value={fee} onChange={(e) => setFee(e.target.value)} error={feeError} />
      <TextField label={t.settings.freeFrom} inputMode="decimal" value={free} onChange={(e) => setFree(e.target.value)} error={freeError} hint={free.trim() ? undefined : t.settings.noFree} />
      <div className="sm:pt-6">
        <Button variant="outline" disabled={!!feeError || !!freeError || save.isPending} onClick={() => void submit()}>
          {t.settings.saveRate}
        </Button>
      </div>
    </fieldset>
  );
}

const count = (min: number, max: number) => z.coerce.number<string>({ error: t.settings.numberInvalid }).int(t.settings.numberInvalid).min(min, t.settings.numberInvalid).max(max, t.settings.numberInvalid);
export const antiFakeSchema = z.object({ maxOpenOrdersPerCustomer: count(1, 20), maxRefusedOrders: count(0, 20), pendingExpiryHours: count(1, 168) });

function AntiFakeForm({ initial }: { initial: { maxOpenOrdersPerCustomer: number; maxRefusedOrders: number; pendingExpiryHours: number } }) {
  const save = useSaveOrderSettings();
  const { register, handleSubmit, formState, setError } = useForm<z.input<typeof antiFakeSchema>, unknown, z.output<typeof antiFakeSchema>>({
    resolver: zodResolver(antiFakeSchema),
    defaultValues: {
      maxOpenOrdersPerCustomer: String(initial.maxOpenOrdersPerCustomer),
      maxRefusedOrders: String(initial.maxRefusedOrders),
      pendingExpiryHours: String(initial.pendingExpiryHours),
    },
  });

  const onSubmit = handleSubmit(async (values) => {
    try {
      await save.mutateAsync(values);
      toast.success(t.settings.antiFakeSaved);
    } catch (error) {
      if (!applyFieldErrors(error, setError, ["maxOpenOrdersPerCustomer", "maxRefusedOrders", "pendingExpiryHours"])) {
        toast.error(describeError(error));
      }
    }
  });

  const e = formState.errors;
  return (
    <form className="grid gap-4 sm:max-w-md" onSubmit={(ev) => void onSubmit(ev)} noValidate>
      <TextField label={t.settings.maxOpen} inputMode="numeric" error={e.maxOpenOrdersPerCustomer?.message} {...register("maxOpenOrdersPerCustomer")} />
      <TextField label={t.settings.maxRefused} hint={t.settings.maxRefusedHint} inputMode="numeric" error={e.maxRefusedOrders?.message} {...register("maxRefusedOrders")} />
      <TextField label={t.settings.expiryHours} inputMode="numeric" error={e.pendingExpiryHours?.message} {...register("pendingExpiryHours")} />
      <div>
        <Button type="submit" disabled={formState.isSubmitting}>
          {t.common.save}
        </Button>
      </div>
    </form>
  );
}

export function DeliveryPage() {
  const rates = useDeliveryRates();
  const settings = useOrderSettings();

  return (
    <>
      <PageHeader title={t.nav.delivery} />
      <div className="grid gap-4">
        <Card>
          <CardHeader>
            <CardTitle>{t.settings.delivery}</CardTitle>
            <p className="text-sm text-muted-foreground">{t.settings.deliveryIntro}</p>
          </CardHeader>
          <CardContent className="grid gap-4">
            {rates.isLoading ? (
              <LoadingState />
            ) : rates.data ? (
              rates.data.map((rate) => <RateRow key={`${rate.countryCode}-${rate.updatedAtUtc}`} rate={rate} />)
            ) : (
              <ErrorState message={describeError(rates.error)} onRetry={() => void rates.refetch()} />
            )}
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>{t.settings.antiFake}</CardTitle>
            <p className="text-sm text-muted-foreground">{t.settings.antiFakeIntro}</p>
          </CardHeader>
          <CardContent>
            {settings.isLoading ? (
              <LoadingState />
            ) : settings.data ? (
              <AntiFakeForm key={settings.data.updatedAtUtc} initial={settings.data} />
            ) : (
              <ErrorState message={describeError(settings.error)} onRetry={() => void settings.refetch()} />
            )}
          </CardContent>
        </Card>
      </div>
    </>
  );
}
