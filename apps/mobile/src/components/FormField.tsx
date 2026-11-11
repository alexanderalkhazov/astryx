import { type Control, Controller, type FieldValues, type Path } from 'react-hook-form';

import { Input, type InputProps } from './Input';

export interface FormFieldProps<T extends FieldValues> extends Omit<InputProps, 'value' | 'onChangeText' | 'error'> {
  control: Control<T>;
  name: Path<T>;
}

/** An `Input` bound to react-hook-form; shows the field's client or server (422) error. */
export function FormField<T extends FieldValues>({ control, name, onBlur, ...input }: FormFieldProps<T>) {
  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => (
        <Input
          ref={field.ref}
          value={field.value as string}
          onChangeText={field.onChange}
          onBlur={(e) => {
            field.onBlur();
            onBlur?.(e);
          }}
          error={fieldState.error?.message}
          {...input}
        />
      )}
    />
  );
}
