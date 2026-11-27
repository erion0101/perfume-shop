import type { Metadata } from "next";
import { ComingSoon } from "@/components/ComingSoon";
import { t } from "@/i18n";

export const metadata: Metadata = { title: t.authenticity.title, robots: { index: false } };

// Phase 2: the batch check (product, brand, "verified", receipt month).
export default function AuthenticityPage() {
  return <ComingSoon title={t.authenticity.title} text={t.pending.authenticity} />;
}
