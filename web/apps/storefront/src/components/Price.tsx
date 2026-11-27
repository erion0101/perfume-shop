import { cn, formatMoney } from "@perfumeshop/ui";
import { t } from "@/i18n";

/** Price with the crossed-out compare-at price when it is higher (VAT included, EUR). */
export function Price({ amount, compareAt, className, from }: { amount: number; compareAt?: number | null; className?: string; from?: boolean }) {
  const reduced = compareAt != null && compareAt > amount;
  return (
    <span className={cn("inline-flex flex-wrap items-baseline gap-x-2", className)}>
      {from ? <span className="text-sm font-normal text-muted">{t.common.from}</span> : null}
      <span className="font-semibold text-text">{formatMoney(amount)}</span>
      {reduced ? (
        <>
          <s className="text-sm font-normal text-subtle">
            <span className="sr-only">{t.product.compareAt}: </span>
            {formatMoney(compareAt)}
          </s>
          <span className="text-sm font-medium text-gold-light">{t.product.save(formatMoney(compareAt - amount))}</span>
        </>
      ) : null}
    </span>
  );
}
