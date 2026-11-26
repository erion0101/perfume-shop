import type { Meta, StoryObj } from "@storybook/react-vite";
import { StatusBadge, type BadgeTone } from "../components/badge";

const map: Record<string, { label: string; tone: BadgeTone }> = {
  Draft: { label: "Draft", tone: "neutral" },
  Published: { label: "Publikuar", tone: "success" },
  LowStock: { label: "Stok i ulët", tone: "warning" },
  OutOfStock: { label: "Pa stok", tone: "danger" },
  Pending: { label: "Në pritje", tone: "info" },
  Featured: { label: "E veçantë", tone: "brand" },
};

const meta = { title: "Basics/StatusBadge", component: StatusBadge, args: { status: "Published", map } } satisfies Meta<typeof StatusBadge>;
export default meta;
type Story = StoryObj<typeof meta>;

export const Single: Story = {};
export const AllTones: Story = {
  render: () => (
    <div className="flex flex-wrap gap-2">
      {Object.keys(map).map((status) => (
        <StatusBadge key={status} status={status} map={map} />
      ))}
      <StatusBadge status="Unknown" map={map} />
    </div>
  ),
};
