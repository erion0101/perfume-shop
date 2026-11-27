import type { Metadata } from "next";
import { ComingSoon } from "@/components/ComingSoon";
import { t } from "@/i18n";

export const metadata: Metadata = { title: t.nav.quiz, robots: { index: false } };

// Phase 2: the scent quiz.
export default function QuizPage() {
  return <ComingSoon title={t.nav.quiz} text={t.pending.quiz} />;
}
