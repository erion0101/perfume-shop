import { ArrowDown, ArrowUp, ArrowUpDown, ChevronLeft, ChevronRight } from "lucide-react";
import type { ReactNode } from "react";
import { cn } from "../lib/cn";
import { useUiTexts } from "../lib/texts";
import { Button } from "./button";
import { EmptyState, ErrorState, LoadingState } from "./states";

export interface DataTableColumn<T> {
  key: string;
  header: string;
  cell: (row: T) => ReactNode;
  /** The API's sort value for this column (server-side sorting). Omit for unsortable columns. */
  sortKey?: string;
  className?: string;
  /** Hidden in the phone (card) layout. */
  hideOnMobile?: boolean;
}

export interface SortState {
  key: string;
  direction: "asc" | "desc";
}

export interface DataTableProps<T> {
  columns: DataTableColumn<T>[];
  rows: T[] | undefined;
  rowKey: (row: T) => string;
  /** Server-side paging: the API's totalCount, page (1-based) and pageSize. */
  total: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  sort?: SortState | null;
  onSortChange?: (sort: SortState) => void;
  /** Filters / search above the table. */
  toolbar?: ReactNode;
  isLoading?: boolean;
  error?: string | null;
  onRetry?: () => void;
  emptyTitle?: string;
  emptyText?: ReactNode;
  onRowClick?: (row: T) => void;
  caption?: string;
}

/**
 * Table for server-side data: the parent keeps page / sort / filters (usually in the URL) and refetches.
 * On phones every row becomes a card (label: value), so it works one-handed in the warehouse.
 */
export function DataTable<T>(props: DataTableProps<T>) {
  const { columns, rows, rowKey, total, page, pageSize, onPageChange, sort, onSortChange, toolbar, isLoading, error, onRetry, emptyTitle, emptyText, onRowClick, caption } = props;
  const texts = useUiTexts();
  const pages = Math.max(1, Math.ceil(total / pageSize));

  function toggleSort(column: DataTableColumn<T>) {
    if (!column.sortKey || !onSortChange) {
      return;
    }

    const direction = sort?.key === column.sortKey && sort.direction === "asc" ? "desc" : "asc";
    onSortChange({ key: column.sortKey, direction });
  }

  let body: ReactNode;
  if (error) {
    body = <ErrorState message={error} onRetry={onRetry} />;
  } else if (isLoading && !rows) {
    body = <LoadingState />;
  } else if (!rows || rows.length === 0) {
    body = <EmptyState title={emptyTitle}>{emptyText}</EmptyState>;
  } else {
    body = (
      <>
        <table className="hidden w-full text-sm md:table">
          {caption ? <caption className="sr-only">{caption}</caption> : null}
          <thead>
            <tr className="border-b border-border text-left text-muted-foreground">
              {columns.map((column) => {
                const active = sort && column.sortKey && sort.key === column.sortKey ? sort.direction : null;
                return (
                  <th
                    key={column.key}
                    scope="col"
                    className={cn("px-3 py-2 font-medium", column.className)}
                    aria-sort={active === "asc" ? "ascending" : active === "desc" ? "descending" : undefined}
                  >
                    {column.sortKey && onSortChange ? (
                      <button type="button" className="inline-flex items-center gap-1 hover:text-foreground" onClick={() => toggleSort(column)}>
                        {column.header}
                        {active === "asc" ? (
                          <ArrowUp className="size-3.5" aria-label={texts.sortAscending} />
                        ) : active === "desc" ? (
                          <ArrowDown className="size-3.5" aria-label={texts.sortDescending} />
                        ) : (
                          <ArrowUpDown className="size-3.5 opacity-40" aria-hidden />
                        )}
                      </button>
                    ) : (
                      column.header
                    )}
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody className={cn(isLoading && "opacity-60")}>
            {rows.map((row) => (
              <tr
                key={rowKey(row)}
                className={cn("border-b border-border last:border-0", onRowClick && "cursor-pointer hover:bg-muted/60")}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
              >
                {columns.map((column) => (
                  <td key={column.key} className={cn("px-3 py-2 align-middle", column.className)}>
                    {column.cell(row)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>

        <ul className={cn("grid gap-2 md:hidden", isLoading && "opacity-60")} aria-label={caption}>
          {rows.map((row) => (
            <li key={rowKey(row)}>
              <div
                role={onRowClick ? "button" : undefined}
                tabIndex={onRowClick ? 0 : undefined}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
                onKeyDown={onRowClick ? (e) => (e.key === "Enter" ? onRowClick(row) : undefined) : undefined}
                className="grid gap-1 rounded-md border border-border p-3 text-sm"
              >
                {columns
                  .filter((c) => !c.hideOnMobile)
                  .map((column) => (
                    <div key={column.key} className="flex justify-between gap-3">
                      <span className="text-muted-foreground">{column.header}</span>
                      <span className="text-right">{column.cell(row)}</span>
                    </div>
                  ))}
              </div>
            </li>
          ))}
        </ul>
      </>
    );
  }

  return (
    <div className="grid gap-3">
      {toolbar ? <div className="flex flex-wrap items-end gap-3">{toolbar}</div> : null}
      {body}
      {total > 0 && !error ? (
        <nav className="flex flex-wrap items-center justify-between gap-2 text-sm text-muted-foreground" aria-label={texts.pageOf(page, pages, total)}>
          <span>{texts.pageOf(page, pages, total)}</span>
          <div className="flex gap-2">
            <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
              <ChevronLeft /> {texts.previous}
            </Button>
            <Button variant="outline" size="sm" disabled={page >= pages} onClick={() => onPageChange(page + 1)}>
              {texts.next} <ChevronRight />
            </Button>
          </div>
        </nav>
      ) : null}
    </div>
  );
}
