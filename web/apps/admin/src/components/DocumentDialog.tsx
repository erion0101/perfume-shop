import { Button, Dialog, ErrorState, LoadingState } from "@perfumeshop/ui";
import { useQuery } from "@tanstack/react-query";
import { Printer } from "lucide-react";
import { useRef, useState, type ReactNode } from "react";
import { t } from "@/i18n";
import { fetchDocument, type PrintableDocument } from "@/lib/documents";
import { describeError } from "@/lib/problem";

/**
 * Shows an API-rendered document (packing slip, receipt, manifest) and prints it. The HTML is fetched with the
 * staff token and shown in a sandboxed iframe (no scripts), so it prints exactly as the API rendered it.
 */
export function DocumentDialog({ document, title, open, onOpenChange }: { document: PrintableDocument; title: string; open: boolean; onOpenChange: (open: boolean) => void }) {
  const frame = useRef<HTMLIFrameElement>(null);
  const html = useQuery({ queryKey: ["document", document], enabled: open, staleTime: 0, gcTime: 0, queryFn: () => fetchDocument(document) });

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      wide
      title={title}
      footer={
        <Button disabled={!html.data} onClick={() => frame.current?.contentWindow?.print()}>
          <Printer /> {t.print.print}
        </Button>
      }
    >
      {html.isLoading ? (
        <LoadingState label={t.print.loading} />
      ) : html.error ? (
        <ErrorState message={describeError(html.error)} onRetry={() => void html.refetch()} />
      ) : (
        <iframe ref={frame} title={title} srcDoc={html.data} sandbox="allow-same-origin allow-modals" className="h-[65dvh] w-full rounded-md border border-border bg-white" />
      )}
    </Dialog>
  );
}

/** A button that opens the document dialog. */
export function PrintButton({ document, title, label, variant = "outline", children }: { document: PrintableDocument; title: string; label: string; variant?: "outline" | "default" | "secondary"; children?: ReactNode }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button variant={variant} onClick={() => setOpen(true)}>
        {children ?? <Printer />} {label}
      </Button>
      {open ? <DocumentDialog document={document} title={title} open={open} onOpenChange={setOpen} /> : null}
    </>
  );
}
