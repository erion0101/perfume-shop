import type { Preview } from "@storybook/react-vite";
import { Toaster } from "../src/components/toast";
import "./storybook.css";

const preview: Preview = {
  parameters: { layout: "padded", controls: { expanded: true } },
  decorators: [
    (Story) => (
      <div className="font-sans text-foreground">
        <Story />
        <Toaster />
      </div>
    ),
  ],
};

export default preview;
