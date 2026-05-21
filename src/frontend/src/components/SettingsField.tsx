import { Check, ChevronDown } from "lucide-react";
import type { SettingFieldPreview } from "../types";
import "./components.css";

interface SettingsFieldProps {
  field: SettingFieldPreview;
}

export function SettingsField({ field }: SettingsFieldProps) {
  const control =
    field.kind === "toggle" ? (
      <span className="settings-field__toggle" aria-hidden="true">
        <Check size={14} />
      </span>
    ) : field.kind === "select" ? (
      <span className="settings-field__select">
        {field.value}
        <ChevronDown aria-hidden="true" size={15} />
      </span>
    ) : (
      <input aria-label={field.label} readOnly value={field.value} />
    );

  return (
    <label className="settings-field">
      <span>
        <strong>{field.label}</strong>
        <small>{field.helper}</small>
      </span>
      {control}
    </label>
  );
}
