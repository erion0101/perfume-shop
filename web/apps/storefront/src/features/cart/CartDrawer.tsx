"use client";

import { cn, formatMoney } from "@perfumeshop/ui";
import Image from "next/image";
import Link from "next/link";
import { Drawer } from "@/components/Drawer";
import { formatSize, label, t } from "@/i18n";
import { MAX_QUANTITY } from "./cart-store";
import { useCart, type CartQuote } from "./CartProvider";

type Line = CartQuote["lines"][number];

function Stepper({ line, name }: { line: Line; name: string }) {
  const { setQuantity, remove, items } = useCart();
  // The cart is the source of truth: the quote may still describe the previous quantity while it reloads.
  const quantity = items.find((i) => i.variantId === line.variantId)?.quantity ?? line.quantity;
  const button = "grid size-11 place-items-center rounded-full border border-line-strong text-text hover:border-gold disabled:opacity-40";
  return (
    <div className="flex items-center gap-2">
      <button type="button" className={button} aria-label={t.cart.decrease(name)} onClick={() => setQuantity(line.variantId, quantity - 1)}>
        <span aria-hidden="true">−</span>
      </button>
      <output className="w-6 text-center tabular-nums" aria-label={t.cart.quantity}>
        {quantity}
      </output>
      <button type="button" className={button} aria-label={t.cart.increase(name)} disabled={quantity >= MAX_QUANTITY} onClick={() => setQuantity(line.variantId, quantity + 1)}>
        <span aria-hidden="true">+</span>
      </button>
      <button type="button" className="ml-auto min-h-11 px-2 text-sm text-muted underline-offset-4 hover:text-gold-light hover:underline" onClick={() => remove(line.variantId)}>
        <span aria-hidden="true">{t.cart.removeShort}</span>
        <span className="sr-only">{t.cart.remove(name)}</span>
      </button>
    </div>
  );
}

/** Totals block: everything from the quote, nothing computed here. */
export function QuoteTotals({ quote }: { quote: CartQuote }) {
  const free = quote.freeDeliveryFrom;
  const progress = free && free > 0 ? Math.min(100, Math.round((quote.subtotal / free) * 100)) : null;
  return (
    <div className="grid gap-3 text-sm">
      {progress !== null ? (
        <div>
          <p className="m-0 mb-2 text-soft" aria-live="polite">
            {quote.amountUntilFreeDelivery && quote.amountUntilFreeDelivery > 0 ? t.cart.untilFree(formatMoney(quote.amountUntilFreeDelivery)) : t.cart.freeReached}
          </p>
          <div className="h-1.5 rounded-[3px] bg-line" role="progressbar" aria-label={t.cart.delivery} aria-valuemin={0} aria-valuemax={100} aria-valuenow={progress}>
            <div className="h-1.5 rounded-[3px] bg-gold transition-[width]" style={{ width: `${progress}%` }} />
          </div>
        </div>
      ) : null}
      <dl className="m-0 grid grid-cols-[1fr_auto] gap-x-4 gap-y-1.5">
        <dt className="text-muted">{t.cart.subtotal}</dt>
        <dd className="m-0 text-right tabular-nums">{formatMoney(quote.subtotal)}</dd>
        <dt className="text-muted">{t.cart.delivery}</dt>
        <dd className="m-0 text-right tabular-nums">{quote.deliveryFee === 0 ? t.cart.deliveryFree : formatMoney(quote.deliveryFee)}</dd>
        <dt className="text-base font-semibold text-heading">{t.cart.total}</dt>
        <dd className="m-0 text-right text-base font-semibold tabular-nums text-heading">{formatMoney(quote.total)}</dd>
      </dl>
      <p className="m-0 text-xs text-subtle">{t.cart.vatIncluded(formatMoney(quote.vatAmount))}</p>
    </div>
  );
}

export function CartLines({ quote, slugs, onNavigate }: { quote: CartQuote; slugs: Map<string, string>; onNavigate?: () => void }) {
  return (
    <ul className="m-0 grid list-none gap-5 p-0">
      {quote.lines.map((line) => {
        const name = [line.brandName, line.fragranceName].filter(Boolean).join(" ") || t.cart.unavailable;
        const size = formatSize(line.sizeValue != null && line.sizeUnit ? { value: line.sizeValue, unit: line.sizeUnit } : null);
        const slug = slugs.get(line.variantId);
        return (
          <li key={line.variantId} className="grid grid-cols-[64px_1fr] gap-4">
            <div className="grid size-16 place-items-center overflow-hidden rounded-xl bg-[radial-gradient(90%_80%_at_50%_30%,#2A1F14,#15110D_75%)]">
              {line.imageUrl ? <Image src={line.imageUrl} alt="" width={64} height={64} className="size-16 object-cover" /> : <span aria-hidden="true" className="h-10 w-7 rounded-md border border-[rgba(241,215,154,.4)] bg-[linear-gradient(160deg,rgba(255,220,160,.5),rgba(140,60,18,.75))]" />}
            </div>
            <div className="grid gap-2">
              <div className="flex justify-between gap-3">
                <div>
                  {slug ? (
                    <Link href={`/parfum/${slug}`} onClick={onNavigate} className="font-display text-lg text-heading hover:text-gold-light">
                      {name}
                    </Link>
                  ) : (
                    <span className="font-display text-lg text-heading">{name}</span>
                  )}
                  <p className="m-0 text-sm text-muted">{[size, label(t.enums.variantType, line.variantType)].filter(Boolean).join(" · ")}</p>
                </div>
                <span className="text-right font-semibold tabular-nums">{line.lineTotal != null ? formatMoney(line.lineTotal) : t.common.none}</span>
              </div>
              {!line.isAvailable ? (
                <p className="m-0 text-sm text-danger" role="status">
                  {(line.problem && t.cart.lineProblem[line.problem]) || t.cart.unavailable}
                </p>
              ) : null}
              <Stepper line={line} name={name} />
            </div>
          </li>
        );
      })}
    </ul>
  );
}

export function CartDrawer() {
  const cart = useCart();
  const { quote, status } = { quote: cart.quote.quote, status: cart.quote.status };
  const slugs = new Map(cart.items.map((i) => [i.variantId, i.slug]));
  const empty = cart.loaded && cart.items.length === 0;

  return (
    <Drawer
      open={cart.isOpen}
      onClose={cart.close}
      title={t.cart.title}
      closeLabel={t.cart.close}
      footer={
        !empty && quote ? (
          <div className="grid gap-4">
            <QuoteTotals quote={quote} />
            <Link
              href="/shporta"
              onClick={cart.close}
              aria-disabled={!quote.canCheckout}
              className={cn("grid min-h-12 place-items-center rounded-full bg-gold px-6 font-semibold text-ground hover:bg-gold-light hover:text-ground", !quote.canCheckout && "pointer-events-none opacity-50")}
            >
              {t.cart.checkout}
            </Link>
            <p className="m-0 text-center text-xs text-subtle">{t.cart.payOnDelivery}</p>
          </div>
        ) : null
      }
    >
      {empty ? (
        <div className="grid gap-4 py-10 text-center">
          <p className="m-0 font-display text-2xl text-heading">{t.cart.empty}</p>
          <p className="m-0 text-muted">{t.cart.emptyHint}</p>
          <Link href="/parfumet" onClick={cart.close} className="mx-auto rounded-full border border-line-strong px-6 py-3 text-text hover:border-gold">
            {t.cart.browse}
          </Link>
        </div>
      ) : status === "error" && !quote ? (
        <div className="grid gap-3 py-8 text-center" role="alert">
          <p className="m-0">{t.cart.quoteFailed}</p>
          <button type="button" onClick={cart.requote} className="mx-auto min-h-11 rounded-full border border-gold px-5 text-gold-light">
            {t.cart.retry}
          </button>
        </div>
      ) : quote ? (
        <div aria-busy={status === "loading"}>
          {status === "loading" ? <p className="sr-only" aria-live="polite">{t.cart.updating}</p> : null}
          <CartLines quote={quote} slugs={slugs} onNavigate={cart.close} />
        </div>
      ) : (
        <p className="py-8 text-center text-muted" aria-live="polite">
          {t.cart.updating}
        </p>
      )}
    </Drawer>
  );
}

export function CartButton() {
  const { count, open, loaded } = useCart();
  return (
    <button
      type="button"
      onClick={open}
      aria-label={t.cart.open(loaded ? count : 0)}
      className="flex h-11 items-center gap-2 rounded-full border border-gold px-4 text-gold-light hover:bg-gold/10"
    >
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
        <path d="M5 8h14l-1.5 12h-11z" />
        <path d="M9 8V6a3 3 0 0 1 6 0v2" />
      </svg>
      <span aria-hidden="true" className="tabular-nums">
        {loaded ? count : 0}
      </span>
    </button>
  );
}
