import type { Meta, StoryObj } from "@storybook/react-vite";
import { BarList, ColumnChart, StatTile } from "../components/charts";
import { formatMoney } from "../lib/money";

const meta = { title: "Data/Charts" } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const days = Array.from({ length: 14 }, (_, i) => {
  const value = [3, 5, 2, 8, 6, 4, 9, 7, 5, 11, 6, 8, 10, 7][i]!;
  return { key: String(i), label: `${13 + i}.11`, value, detail: `${value} porosi · ${formatMoney(value * 41.9)}` };
});

export const OrdersPerDay: Story = {
  render: () => <ColumnChart title="Porositë, 14 ditët e fundit" data={days} onSelect={() => {}} />,
};

export const TopSellers: Story = {
  render: () => (
    <div className="max-w-xl">
      <BarList
        title="Më të shiturat (30 ditë)"
        data={[
          { key: "1", label: "Lattafa Khamrah", value: 42, detail: "42 copë · 1.675,80 €" },
          { key: "2", label: "Lattafa Asad", value: 31, detail: "31 copë" },
          { key: "3", label: "Armaf Club de Nuit Intense Man", value: 18, detail: "18 copë" },
          { key: "4", label: "Rasasi Hawas", value: 9, detail: "9 copë" },
        ]}
        onSelect={() => {}}
      />
    </div>
  ),
};

export const Tiles: Story = {
  render: () => (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
      <StatTile label="Porosi të reja sot" value={12} />
      <StatTile label="Për t'u konfirmuar" value={4} tone="warning" hint="Më e vjetra 3 orë" />
      <StatTile label="Kuponë të dështuar" value={1} tone="danger" />
      <StatTile label="Mbledhur sot" value={formatMoney(503.8)} tone="success" />
    </div>
  ),
};

export const Empty: Story = {
  render: () => <ColumnChart title="Porositë" data={days.map((d) => ({ ...d, value: 0 }))} />,
};
