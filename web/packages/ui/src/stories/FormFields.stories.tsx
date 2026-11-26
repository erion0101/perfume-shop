import type { Meta, StoryObj } from "@storybook/react-vite";
import { useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { Button } from "../components/button";
import { SelectField, TextareaField, TextField } from "../components/form-field";
import { MoneyField } from "../components/money-input";
import { toast } from "../components/toast";

const meta = { title: "Forms/Fields" } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

export const TextFields: Story = {
  render: () => (
    <div className="grid max-w-md gap-4">
      <TextField label="SKU" placeholder="LAT-KHA-100" hint="I vetëm për çdo variant." required />
      <TextField label="EAN" defaultValue="12345" error="EAN duhet të ketë 8 ose 13 shifra." />
      <TextareaField label="Përshkrimi (shqip)" rows={4} />
      <SelectField label="Gjinia" placeholder="Zgjidhni…" options={[{ value: "Male", label: "Burra" }, { value: "Female", label: "Gra" }, { value: "Unisex", label: "Unisex" }]} />
    </div>
  ),
};

export const Money: Story = {
  render: function MoneyStory() {
    const [value, setValue] = useState<number | null>(39.9);
    return (
      <div className="grid max-w-xs gap-2">
        <MoneyField label="Çmimi (me TVSH)" value={value} onChange={setValue} hint="Shkruani 39,90 ose 39.90" />
        <p className="text-sm text-muted-foreground">Vlera: {String(value)}</p>
      </div>
    );
  },
};

interface VariantForm {
  sku: string;
  price: number | null;
}

/** react-hook-form with validation (the admin uses zod schemas through @hookform/resolvers). */
export const WithReactHookForm: Story = {
  render: function FormStory() {
    const { register, control, handleSubmit, formState } = useForm<VariantForm>({ defaultValues: { sku: "", price: null } });
    return (
      <form className="grid max-w-md gap-4" onSubmit={handleSubmit((v) => toast.success(`U ruajt: ${v.sku} – ${v.price}`))} noValidate>
        <TextField label="SKU" required error={formState.errors.sku?.message} {...register("sku", { required: "SKU është i detyrueshëm." })} />
        <Controller
          control={control}
          name="price"
          rules={{ validate: (v) => (v !== null && !Number.isNaN(v) && v > 0) || "Shkruani një çmim më të madh se 0." }}
          render={({ field, fieldState }) => <MoneyField label="Çmimi" required error={fieldState.error?.message} {...field} />}
        />
        <Button type="submit">Ruaj</Button>
      </form>
    );
  },
};
