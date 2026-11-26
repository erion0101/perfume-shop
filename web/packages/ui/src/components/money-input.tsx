import { useState, type ComponentProps } from "react";
import { cn } from "../lib/cn";
import { moneyToInput, parseMoney } from "../lib/money";
import { FormField } from "./form-field";
import { Input } from "./input";

type MoneyInputProps = Omit<ComponentProps<"input">, "value" | "onChange" | "type"> & {
  /** EUR, VAT included. null = empty. NaN while the text is not a number (lets zod report it). */
  value: number | null | undefined;
  onChange: (value: number | null) => void;
};

/**
 * EUR amount input: accepts "39,90" or "39.90", shows the € sign, opens the decimal keypad on phones,
 * and rewrites the text as "39,90" on blur.
 */
export function MoneyInput({ value, onChange, onBlur, onFocus, className, ...props }: MoneyInputProps) {
  const [text, setText] = useState(() => moneyToInput(value));
  const [focused, setFocused] = useState(false);
  const [shown, setShown] = useState(value);

  // A new value from outside (form reset, loaded data) replaces the text unless the user is typing.
  if (!focused && !Object.is(value, shown)) {
    setShown(value);
    if (!(typeof value === "number" && Number.isNaN(value))) {
      setText(moneyToInput(value));
    }
  }

  return (
    <div className="relative">
      <Input
        {...props}
        type="text"
        inputMode="decimal"
        autoComplete="off"
        value={text}
        className={cn("pr-8 text-right tabular-nums", className)}
        onFocus={(e) => {
          setFocused(true);
          onFocus?.(e);
        }}
        onChange={(e) => {
          setText(e.target.value);
          onChange(parseMoney(e.target.value));
        }}
        onBlur={(e) => {
          setFocused(false);
          const parsed = parseMoney(text);
          if (parsed !== null && !Number.isNaN(parsed)) {
            setText(moneyToInput(parsed));
          }
          onBlur?.(e);
        }}
      />
      <span className="pointer-events-none absolute inset-y-0 right-3 flex items-center text-sm text-muted-foreground" aria-hidden>
        €
      </span>
    </div>
  );
}

/** Use with react-hook-form's Controller: `<Controller name="price" render={({ field, fieldState }) => <MoneyField label="Çmimi" error={fieldState.error?.message} {...field} />} />` */
export function MoneyField({
  label,
  error,
  hint,
  required,
  className,
  ...input
}: MoneyInputProps & { label: string; error?: string; hint?: string; required?: boolean }) {
  return (
    <FormField label={label} error={error} hint={hint} required={required} className={className}>
      {({ id, describedBy, invalid }) => <MoneyInput id={id} aria-describedby={describedBy} aria-invalid={invalid || undefined} {...input} />}
    </FormField>
  );
}
