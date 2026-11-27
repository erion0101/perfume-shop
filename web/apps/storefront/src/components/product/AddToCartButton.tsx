"use client";

import { cn } from "@perfumeshop/ui";
import { useState } from "react";
import { useCart } from "@/features/cart/CartProvider";
import { t } from "@/i18n";

/** Adds one variant and opens the cart drawer (which re-quotes prices and stock). */
export function AddToCartButton({ variantId, slug, name, compact, disabled, quantity = 1, className }: { variantId: string; slug: string; name: string; compact?: boolean; disabled?: boolean; quantity?: number; className?: string }) {
  const cart = useCart();
  const [announce, setAnnounce] = useState("");
  return (
    <>
      <button
        type="button"
        disabled={disabled || !cart.loaded}
        aria-label={compact ? t.cart.addAria(name) : undefined}
        onClick={() => {
          cart.add({ variantId, slug }, quantity);
          setAnnounce(t.cart.added(name));
          cart.open();
        }}
        className={cn(
          compact
            ? "min-h-11 rounded-full border border-gold bg-transparent px-[18px] text-sm font-medium text-gold-light hover:bg-gold/10"
            : "min-h-[52px] w-full rounded-full bg-gold px-8 text-base font-semibold text-ground hover:bg-gold-light",
          "disabled:cursor-not-allowed disabled:opacity-50",
          className,
        )}
      >
        {compact ? t.cart.add : t.cart.addToCart}
      </button>
      <span className="sr-only" aria-live="polite">
        {announce}
      </span>
    </>
  );
}
