import { useId, type ComponentProps, type ReactNode } from "react";
import { cn } from "../lib/cn";
import { useUiTexts } from "../lib/texts";
import { Input, Label, Select, Textarea } from "./input";

interface FieldOwnProps {
  label: string;
  /** Validation message (react-hook-form `errors.x?.message`). */
  error?: string;
  hint?: ReactNode;
  required?: boolean;
  className?: string;
}

/** Label + control + hint + error, wired for screen readers (aria-invalid, aria-describedby). */
export function FormField({
  label,
  error,
  hint,
  required,
  className,
  children,
}: FieldOwnProps & { children: (ids: { id: string; describedBy?: string; invalid: boolean }) => ReactNode }) {
  const texts = useUiTexts();
  const id = useId();
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  const describedBy = [hint ? hintId : null, error ? errorId : null].filter(Boolean).join(" ") || undefined;

  return (
    <div className={cn("grid gap-1.5", className)}>
      <Label htmlFor={id}>
        {label}
        {required ? (
          <>
            <span className="text-destructive" aria-hidden>
              {" "}
              *
            </span>
            <span className="sr-only"> ({texts.required})</span>
          </>
        ) : null}
      </Label>
      {children({ id, describedBy, invalid: Boolean(error) })}
      {hint ? (
        <p id={hintId} className="text-xs text-muted-foreground">
          {hint}
        </p>
      ) : null}
      {error ? (
        <p id={errorId} role="alert" className="text-xs font-medium text-destructive">
          {error}
        </p>
      ) : null}
    </div>
  );
}

type InputProps = Omit<ComponentProps<"input">, "className"> & FieldOwnProps;

/** `<TextField label="SKU" error={errors.sku?.message} {...register("sku")} />` */
export function TextField({ label, error, hint, required, className, ...input }: InputProps) {
  return (
    <FormField label={label} error={error} hint={hint} required={required} className={className}>
      {({ id, describedBy, invalid }) => <Input id={id} aria-describedby={describedBy} aria-invalid={invalid || undefined} required={required} {...input} />}
    </FormField>
  );
}

export function TextareaField({ label, error, hint, required, className, ...textarea }: Omit<ComponentProps<"textarea">, "className"> & FieldOwnProps) {
  return (
    <FormField label={label} error={error} hint={hint} required={required} className={className}>
      {({ id, describedBy, invalid }) => (
        <Textarea id={id} aria-describedby={describedBy} aria-invalid={invalid || undefined} required={required} {...textarea} />
      )}
    </FormField>
  );
}

export interface SelectOption {
  value: string;
  label: string;
}

export function SelectField({
  label,
  error,
  hint,
  required,
  className,
  options,
  placeholder,
  ...select
}: Omit<ComponentProps<"select">, "className"> & FieldOwnProps & { options: readonly SelectOption[]; placeholder?: string }) {
  return (
    <FormField label={label} error={error} hint={hint} required={required} className={className}>
      {({ id, describedBy, invalid }) => (
        <Select id={id} aria-describedby={describedBy} aria-invalid={invalid || undefined} required={required} {...select}>
          {placeholder !== undefined ? <option value="">{placeholder}</option> : null}
          {options.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </Select>
      )}
    </FormField>
  );
}
