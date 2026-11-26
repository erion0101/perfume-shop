import { Input, cn } from "@perfumeshop/ui";
import { Search } from "lucide-react";
import { useEffect, useEffectEvent, useState } from "react";
import { t } from "@/i18n";

/** Search box that reports the text 300 ms after the user stops typing (one request, not one per key). */
export function SearchInput({ value, onChange, className, placeholder }: { value: string; onChange: (value: string) => void; className?: string; placeholder?: string }) {
  const [text, setText] = useState(value);
  const report = useEffectEvent((next: string) => onChange(next));

  useEffect(() => {
    if (text === value) {
      return;
    }

    const timer = window.setTimeout(() => report(text), 300);
    return () => window.clearTimeout(timer);
  }, [text, value]);

  return (
    <div className={cn("relative w-full sm:max-w-xs", className)}>
      <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
      <Input type="search" className="pl-9" aria-label={t.common.search} placeholder={placeholder ?? t.common.searchPlaceholder} value={text} onChange={(e) => setText(e.target.value)} />
    </div>
  );
}
