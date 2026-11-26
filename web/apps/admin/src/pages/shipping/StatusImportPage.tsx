import { Button, Card, CardContent, Select, StatusBadge, toast } from "@perfumeshop/ui";
import { Upload } from "lucide-react";
import { useState } from "react";
import { CsvFileInput } from "@/components/CsvFileInput";
import { PageHeader } from "@/components/PageHeader";
import { useCouriers, useStatusImport, type StatusImportResult } from "@/features/shipping/api";
import { importOutcomeBadges } from "@/features/statuses";
import { t } from "@/i18n";
import { describeError } from "@/lib/problem";

export function ImportResult({ result }: { result: StatusImportResult }) {
  return (
    <Card>
      <CardContent className="grid gap-3">
        <h2 className="font-semibold">{t.statusImport.result}</h2>
        {result.replayed ? <p className="rounded-md bg-amber-50 p-2 text-sm">{t.statusImport.replayed}</p> : null}
        <dl className="grid grid-cols-3 gap-2 text-center">
          {[
            [t.statusImport.applied, result.applied, "text-emerald-700"],
            [t.statusImport.alreadyApplied, result.alreadyApplied, ""],
            [t.statusImport.failed, result.failed, result.failed > 0 ? "text-destructive" : ""],
          ].map(([name, value, tone]) => (
            <div key={name as string} className="rounded-md bg-muted p-2">
              <dt className="text-xs text-muted-foreground">{name}</dt>
              <dd className={`text-xl font-semibold tabular-nums ${tone}`}>{value}</dd>
            </div>
          ))}
        </dl>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[36rem] text-sm" aria-label={t.statusImport.result}>
            <thead>
              <tr className="border-b border-border text-left text-muted-foreground">
                <th className="px-2 py-1.5 font-medium">{t.statusImport.line}</th>
                <th className="px-2 py-1.5 font-medium">{t.statusImport.tracking}</th>
                <th className="px-2 py-1.5 font-medium">{t.statusImport.status}</th>
                <th className="px-2 py-1.5 font-medium">{t.statusImport.outcome}</th>
                <th className="px-2 py-1.5 font-medium">{t.statusImport.note}</th>
              </tr>
            </thead>
            <tbody>
              {result.lines.map((line) => (
                <tr key={line.lineNumber} className={`border-b border-border last:border-0 ${line.outcome === "Applied" || line.outcome === "AlreadyApplied" ? "" : "bg-red-50"}`}>
                  <td className="px-2 py-1.5 tabular-nums">{line.lineNumber}</td>
                  <td className="px-2 py-1.5 font-medium">{line.trackingNumber}</td>
                  <td className="px-2 py-1.5">{line.status}</td>
                  <td className="px-2 py-1.5">
                    <StatusBadge status={line.outcome} map={importOutcomeBadges} />
                  </td>
                  <td className="px-2 py-1.5 text-muted-foreground">{line.note ?? ""}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </CardContent>
    </Card>
  );
}

export function StatusImportPage() {
  const couriers = useCouriers();
  const upload = useStatusImport();
  const [courierId, setCourierId] = useState("");
  const [file, setFile] = useState<{ name: string; text: string } | null>(null);
  const [result, setResult] = useState<StatusImportResult | null>(null);

  async function submit() {
    if (!file) {
      return;
    }

    try {
      setResult(await upload.mutateAsync({ courierId, csv: file.text }));
    } catch (error) {
      toast.error(describeError(error));
    }
  }

  return (
    <>
      <PageHeader title={t.statusImport.title} description={t.statusImport.intro} />
      <div className="grid gap-4">
        <Card>
          <CardContent className="grid gap-4">
            <label className="grid gap-1.5 text-sm font-medium sm:w-64">
              {t.statusImport.courier}
              <Select aria-label={t.statusImport.courier} value={courierId} onChange={(e) => setCourierId(e.target.value)}>
                <option value="">{t.common.choose}</option>
                {(couriers.data ?? []).map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Select>
            </label>
            <CsvFileInput
              label={t.statusImport.file}
              onLoaded={(loaded) => {
                setFile(loaded);
                setResult(null);
              }}
            />
            <div>
              <Button disabled={!courierId || !file || upload.isPending} onClick={() => void submit()}>
                <Upload /> {upload.isPending ? t.statusImport.uploading : t.statusImport.upload}
              </Button>
            </div>
          </CardContent>
        </Card>
        {result ? <ImportResult result={result} /> : null}
      </div>
    </>
  );
}
