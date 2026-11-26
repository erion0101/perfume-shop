import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { DataTable } from "./data-table";
import { ConfirmDialog } from "./dialog";
import { TextField } from "./form-field";
import { Button } from "./button";
import { MoneyInput } from "./money-input";
import { StatusBadge } from "./badge";

interface Row {
  id: string;
  name: string;
}

const rows: Row[] = [
  { id: "1", name: "Khamrah" },
  { id: "2", name: "Asad" },
];

describe("DataTable", () => {
  it("renders rows and pages on the server", async () => {
    const onPageChange = vi.fn();
    render(
      <DataTable<Row>
        columns={[{ key: "name", header: "Name", cell: (r) => r.name, sortKey: "name" }]}
        rows={rows}
        rowKey={(r) => r.id}
        total={60}
        page={1}
        pageSize={25}
        onPageChange={onPageChange}
        sort={{ key: "name", direction: "asc" }}
        onSortChange={() => {}}
      />,
    );

    expect(screen.getAllByText("Khamrah")).toHaveLength(2); // table + phone card
    expect(screen.getByText("Page 1 of 3 · 60 results")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Previous/ })).toBeDisabled();
    await userEvent.click(screen.getByRole("button", { name: /Next/ }));
    expect(onPageChange).toHaveBeenCalledWith(2);
    expect(screen.getByRole("columnheader", { name: /Name/ })).toHaveAttribute("aria-sort", "ascending");
  });

  it("asks the parent to sort the other way", async () => {
    const onSortChange = vi.fn();
    render(
      <DataTable<Row>
        columns={[{ key: "name", header: "Name", cell: (r) => r.name, sortKey: "name" }]}
        rows={rows}
        rowKey={(r) => r.id}
        total={2}
        page={1}
        pageSize={25}
        onPageChange={() => {}}
        sort={{ key: "name", direction: "asc" }}
        onSortChange={onSortChange}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: /Name/ }));
    expect(onSortChange).toHaveBeenCalledWith({ key: "name", direction: "desc" });
  });

  it("shows empty, loading and error states", async () => {
    const onRetry = vi.fn();
    const props = { columns: [], rowKey: (r: Row) => r.id, total: 0, page: 1, pageSize: 25, onPageChange: () => {} };
    const { rerender } = render(<DataTable<Row> {...props} rows={[]} emptyTitle="No fragrances" />);
    expect(screen.getByText("No fragrances")).toBeInTheDocument();

    rerender(<DataTable<Row> {...props} rows={undefined} isLoading />);
    expect(screen.getByRole("status")).toHaveTextContent("Loading");

    rerender(<DataTable<Row> {...props} rows={undefined} error="Server down" onRetry={onRetry} />);
    expect(screen.getByRole("alert")).toHaveTextContent("Server down");
    await userEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(onRetry).toHaveBeenCalled();
  });
});

describe("MoneyInput", () => {
  function Harness({ onValue }: { onValue: (v: number | null) => void }) {
    const [value, setValue] = useState<number | null>(39.9);
    return (
      <MoneyInput
        aria-label="Price"
        value={value}
        onChange={(v) => {
          setValue(v);
          onValue(v);
        }}
      />
    );
  }

  it("shows the value with a comma, accepts typed amounts and reformats on blur", async () => {
    const onValue = vi.fn();
    render(<Harness onValue={onValue} />);
    const input = screen.getByLabelText("Price");
    expect(input).toHaveValue("39,90");

    await userEvent.clear(input);
    await userEvent.type(input, "45.5");
    expect(onValue).toHaveBeenLastCalledWith(45.5);
    await userEvent.tab();
    expect(input).toHaveValue("45,50");

    await userEvent.clear(input);
    expect(onValue).toHaveBeenLastCalledWith(null);
  });
});

describe("TextField", () => {
  it("connects the label, hint and error", () => {
    render(<TextField label="SKU" hint="Unique" error="Required" required />);
    const input = screen.getByLabelText(/SKU/);
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input).toHaveAccessibleDescription("Unique Required");
  });
});

describe("ConfirmDialog", () => {
  it("runs the action only after confirming and closes when it succeeds", async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined);
    render(<ConfirmDialog trigger={<Button>Publish</Button>} title="Publish fragrance?" confirmLabel="Yes, publish" onConfirm={onConfirm} />);

    await userEvent.click(screen.getByRole("button", { name: "Publish" }));
    expect(screen.getByRole("alertdialog")).toHaveTextContent("Publish fragrance?");
    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onConfirm).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole("button", { name: "Publish" }));
    await userEvent.click(screen.getByRole("button", { name: "Yes, publish" }));
    expect(onConfirm).toHaveBeenCalledOnce();
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });

  it("stays open when the action fails", async () => {
    render(<ConfirmDialog trigger={<Button>Delete</Button>} title="Delete?" destructive onConfirm={() => Promise.reject(new Error("409"))} />);

    await userEvent.click(screen.getByRole("button", { name: "Delete" }));
    await userEvent.click(screen.getByRole("button", { name: "Confirm" }));
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
  });
});

describe("StatusBadge", () => {
  it("uses the app's label for a status and falls back to the raw value", () => {
    const map = { Published: { label: "Publikuar", tone: "success" as const } };
    const { rerender } = render(<StatusBadge status="Published" map={map} />);
    expect(screen.getByText("Publikuar")).toBeInTheDocument();
    rerender(<StatusBadge status={"Archived" as "Published"} map={map} />);
    expect(screen.getByText("Archived")).toBeInTheDocument();
  });
});
