import { EmptyState, Button } from "@perfumeshop/ui";
import { Link } from "react-router";
import { t } from "@/i18n";

export function NotFoundPage() {
  return (
    <EmptyState title={t.httpErrors.notFound}>
      <Button asChild variant="outline" size="sm">
        <Link to="/">{t.common.back}</Link>
      </Button>
    </EmptyState>
  );
}
