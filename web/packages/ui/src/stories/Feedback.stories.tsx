import type { Meta, StoryObj } from "@storybook/react-vite";
import { useState } from "react";
import { Button } from "../components/button";
import { ConfirmDialog, Dialog } from "../components/dialog";
import { EmptyState, ErrorState, LoadingState, Skeleton } from "../components/states";
import { toast } from "../components/toast";

const meta = { title: "Feedback/Dialogs, toasts and states" } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

export const Confirm: Story = {
  render: () => (
    <div className="flex gap-2">
      <ConfirmDialog
        trigger={<Button>Publiko</Button>}
        title="Ta publikojmë parfumin?"
        description="Do të shfaqet në dyqan menjëherë."
        confirmLabel="Po, publiko"
        onConfirm={() => new Promise((resolve) => setTimeout(resolve, 600)).then(() => toast.success("U publikua"))}
      />
      <ConfirmDialog
        trigger={<Button variant="destructive">Fshi markën</Button>}
        title="Ta fshijmë markën?"
        destructive
        confirmLabel="Fshi"
        onConfirm={() => {
          toast.error("Marka ka parfume: fshini ose zhvendosni ato më parë.");
          throw new Error("409");
        }}
      />
    </div>
  ),
};

export const PlainDialog: Story = {
  render: function DialogStory() {
    const [open, setOpen] = useState(false);
    return (
      <>
        <Button onClick={() => setOpen(true)}>Hap</Button>
        <Dialog open={open} onOpenChange={setOpen} title="Rregullim i stokut" description="Shkruani sasinë dhe arsyen." footer={<Button onClick={() => setOpen(false)}>Ruaj</Button>}>
          <p className="text-sm">Përmbajtja e formës.</p>
        </Dialog>
      </>
    );
  },
};

export const Toasts: Story = {
  render: () => (
    <div className="flex flex-wrap gap-2">
      <Button onClick={() => toast.success("U ruajt")}>Sukses</Button>
      <Button variant="destructive" onClick={() => toast.error("Ky SKU përdoret tashmë.")}>Gabim</Button>
      <Button variant="outline" onClick={() => toast.info("Kanë mbetur 4.312 kërkesa Fragella këtë muaj.")}>Info</Button>
    </div>
  ),
};

export const States: Story = {
  render: () => (
    <div className="grid gap-6 md:grid-cols-3">
      <LoadingState label="Duke ngarkuar…" />
      <EmptyState title="Asnjë rezultat" action={<Button size="sm">Shto</Button>}>Provoni një kërkim tjetër.</EmptyState>
      <ErrorState message="Lidhja me serverin dështoi." onRetry={() => {}} />
      <div className="grid gap-2 md:col-span-3">
        <Skeleton className="h-6 w-1/3" />
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-2/3" />
      </div>
    </div>
  ),
};
