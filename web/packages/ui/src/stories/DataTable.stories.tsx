import type { Meta, StoryObj } from "@storybook/react-vite";
import { useMemo, useState } from "react";
import { StatusBadge } from "../components/badge";
import { DataTable, type SortState } from "../components/data-table";
import { Input } from "../components/input";
import { formatMoney } from "../lib/money";

interface Row {
  id: string;
  name: string;
  brand: string;
  price: number;
  status: "Draft" | "Published";
}

const all: Row[] = Array.from({ length: 57 }, (_, i) => ({
  id: String(i),
  name: ["Khamrah", "Asad", "Hawas", "Club de Nuit", "Ana Abiyedh"][i % 5]! + ` ${i + 1}`,
  brand: ["Lattafa", "Lattafa", "Rasasi", "Armaf", "Lattafa"][i % 5]!,
  price: 19.9 + (i % 7) * 5,
  status: i % 3 === 0 ? "Draft" : "Published",
}));

const meta = { title: "Data/DataTable" } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/** Paging, sorting and filtering happen "on the server" (here: in the story), as the admin does with the API. */
export const ServerSide: Story = {
  render: function TableStory() {
    const [page, setPage] = useState(1);
    const [sort, setSort] = useState<SortState>({ key: "name", direction: "asc" });
    const [search, setSearch] = useState("");
    const filtered = useMemo(() => {
      const rows = all.filter((r) => r.name.toLowerCase().includes(search.toLowerCase()));
      return rows.sort((a, b) => {
        const k = sort.key as keyof Row;
        const cmp = String(a[k]).localeCompare(String(b[k]), undefined, { numeric: true });
        return sort.direction === "asc" ? cmp : -cmp;
      });
    }, [search, sort]);

    return (
      <DataTable<Row>
        caption="Parfumet"
        toolbar={<Input className="max-w-xs" placeholder="Kërko…" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} />}
        columns={[
          { key: "name", header: "Emri", cell: (r) => r.name, sortKey: "name" },
          { key: "brand", header: "Marka", cell: (r) => r.brand, sortKey: "brand" },
          { key: "price", header: "Çmimi", cell: (r) => formatMoney(r.price), sortKey: "price", className: "text-right" },
          { key: "status", header: "Statusi", cell: (r) => <StatusBadge status={r.status} map={{ Draft: { label: "Draft", tone: "neutral" }, Published: { label: "Publikuar", tone: "success" } }} /> },
        ]}
        rows={filtered.slice((page - 1) * 10, page * 10)}
        rowKey={(r) => r.id}
        total={filtered.length}
        page={page}
        pageSize={10}
        onPageChange={setPage}
        sort={sort}
        onSortChange={setSort}
      />
    );
  },
};

export const Loading: Story = {
  render: () => <DataTable<Row> columns={[]} rows={undefined} isLoading rowKey={(r) => r.id} total={0} page={1} pageSize={10} onPageChange={() => {}} />,
};

export const Empty: Story = {
  render: () => (
    <DataTable<Row> columns={[]} rows={[]} rowKey={(r) => r.id} total={0} page={1} pageSize={10} onPageChange={() => {}} emptyTitle="Asnjë parfum" emptyText="Importoni nga Fragella për të filluar." />
  ),
};

export const Error: Story = {
  render: () => (
    <DataTable<Row> columns={[]} rows={undefined} error="Serveri nuk përgjigjet." onRetry={() => {}} rowKey={(r) => r.id} total={0} page={1} pageSize={10} onPageChange={() => {}} />
  ),
};
