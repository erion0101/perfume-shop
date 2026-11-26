import type { Meta, StoryObj } from "@storybook/react-vite";
import { Plus } from "lucide-react";
import { Button } from "../components/button";

const meta = { title: "Basics/Button", component: Button, args: { children: "Ruaj" } } satisfies Meta<typeof Button>;
export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
export const Variants: Story = {
  render: () => (
    <div className="flex flex-wrap gap-2">
      <Button>Ruaj</Button>
      <Button variant="secondary">Dytësor</Button>
      <Button variant="outline">Anulo</Button>
      <Button variant="ghost">Fantazmë</Button>
      <Button variant="destructive">Fshi</Button>
      <Button variant="link">Lidhje</Button>
      <Button size="sm">
        <Plus /> E vogël
      </Button>
      <Button disabled>Çaktivizuar</Button>
    </div>
  ),
};
