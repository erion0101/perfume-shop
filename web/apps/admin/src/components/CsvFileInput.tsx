import { Button } from "@perfumeshop/ui";
import { FileUp } from "lucide-react";
import { useRef, useState } from "react";
import { t } from "@/i18n";
import { previewCsv, type CsvPreview } from "@/lib/csv";

const maxBytes = 1024 * 1024;

/** Picks a CSV file, reads it in the browser and shows the first rows before anything is uploaded. */
export function CsvFileInput({ label, onLoaded }: { label: string; onLoaded: (file: { name: string; text: string } | null) => void }) {
  const input = useRef<HTMLInputElement>(null);
  const [preview, setPreview] = useState<(CsvPreview & { name: string }) | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function load(file: File | undefined) {
    setError(null);
    setPreview(null);
    onLoaded(null);
    if (!file) {
      return;
    }

    if (file.size > maxBytes) {
      setError(t.csv.tooLarge);
      return;
    }

    const text = await file.text();
    const parsed = previewCsv(text);
    if (parsed.header.length < 2) {
      setError(t.csv.unreadable);
      return;
    }

    setPreview({ ...parsed, name: file.name });
    onLoaded({ name: file.name, text });
  }

  return (
    <div className="grid gap-2">
      <span className="text-sm font-medium">{label}</span>
      <div className="flex flex-wrap items-center gap-2">
        <input ref={input} type="file" accept=".csv,text/csv,text/plain" className="sr-only" aria-label={label} onChange={(e) => void load(e.target.files?.[0])} />
        <Button variant="outline" onClick={() => input.current?.click()}>
          <FileUp /> {t.csv.chooseFile}
        </Button>
        {preview ? (
          <span className="text-sm text-muted-foreground">
            {preview.name} · {t.csv.rows(preview.total)}
          </span>
        ) : null}
      </div>
      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : null}
      {preview ? (
        <div className="overflow-x-auto rounded-md border border-border">
          <table className="w-full text-xs" aria-label={t.statusImport.preview}>
            <thead>
              <tr className="bg-muted">
                {preview.header.map((h, i) => (
                  <th key={i} className="px-2 py-1 text-left font-medium">
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {preview.rows.map((row, r) => (
                <tr key={r} className="border-t border-border">
                  {row.map((cell, c) => (
                    <td key={c} className="px-2 py-1">
                      {cell}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
          {preview.total > preview.rows.length ? <p className="px-2 py-1 text-xs text-muted-foreground">{t.csv.previewNote}</p> : null}
        </div>
      ) : null}
    </div>
  );
}
