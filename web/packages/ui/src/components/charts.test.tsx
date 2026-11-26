import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { BarList, ColumnChart } from "./charts";

const data = [
  { key: "a", label: "24.11", value: 2 },
  { key: "b", label: "25.11", value: 9, detail: "9 porosi" },
  { key: "c", label: "26.11", value: 4 },
];

describe("ColumnChart", () => {
  it("names every column for screen readers and shows a tooltip on hover", async () => {
    const onSelect = vi.fn();
    render(<ColumnChart title="Orders" data={data} onSelect={onSelect} />);

    const column = screen.getByRole("button", { name: "25.11: 9 porosi" });
    await userEvent.hover(column);
    expect(screen.getByRole("tooltip")).toHaveTextContent("25.11: 9 porosi");
    await userEvent.click(column);
    expect(onSelect).toHaveBeenCalledWith(data[1]);
  });

  it("switches to a table view", async () => {
    render(<ColumnChart title="Orders" data={data} />);
    await userEvent.click(screen.getByRole("button", { name: "Show as table" }));
    expect(screen.getByRole("table", { name: "Orders" })).toHaveTextContent("26.11");
  });
});

describe("BarList", () => {
  it("lists items with their values", async () => {
    const onSelect = vi.fn();
    render(<BarList title="Top" data={[{ key: "x", label: "Khamrah", value: 5, detail: "5 copë" }]} onSelect={onSelect} />);
    await userEvent.click(screen.getByRole("button", { name: /Khamrah/ }));
    expect(onSelect).toHaveBeenCalled();
    expect(screen.getByText("5 copë")).toBeInTheDocument();
  });
});
