"use client";

import { useEffect, useRef } from "react";
import { RotateCcw } from "lucide-react";
import {
  useGalleryStore,
  DISPLAY_FIELD_LABELS,
  type DisplayField,
} from "@/store/useGalleryStore";

const FIELDS: DisplayField[] = [
  "identifier",
  "title",
  "actress",
  "maker",
  "label",
  "releaseDate",
  "favorite",
  "rating",
  "status",
];

interface Props {
  onClose: () => void;
}

export function DisplaySettingsPanel({ onClose }: Props) {
  const { displayFields, setDisplayField, resetDisplayFields } = useGalleryStore();
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        onClose();
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [onClose]);

  return (
    <div
      ref={ref}
      className="absolute right-0 top-full mt-2 z-50 w-48
        bg-background/98 backdrop-blur border border-border/60
        rounded-2xl shadow-2xl p-3 flex flex-col gap-0.5"
    >
      <div className="flex items-center justify-between px-1 mb-1.5">
        <span className="text-[10px] font-semibold text-muted-foreground uppercase tracking-widest">
          表示項目
        </span>
        <button
          onClick={resetDisplayFields}
          className="text-muted-foreground hover:text-foreground transition-colors"
          title="デフォルトに戻す"
        >
          <RotateCcw className="w-3 h-3" />
        </button>
      </div>

      {FIELDS.map((field) => (
        <label
          key={field}
          className="flex items-center gap-2.5 px-2 py-1.5 rounded-xl hover:bg-muted/50 cursor-pointer transition-colors"
        >
          <input
            type="checkbox"
            checked={displayFields[field]}
            onChange={(e) => setDisplayField(field, e.target.checked)}
            className="w-3.5 h-3.5 rounded accent-primary flex-none"
          />
          <span className="text-[13px]">{DISPLAY_FIELD_LABELS[field]}</span>
        </label>
      ))}
    </div>
  );
}
