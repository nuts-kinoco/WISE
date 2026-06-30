/**
 * SQLite stores DateTime as UTC without 'Z' suffix.
 * JS treats strings without timezone as LOCAL time, causing a 9-hour JST offset error.
 * This helper ensures the string is parsed as UTC.
 */
export function parseUtcDate(dateStr: string | null | undefined): Date {
  if (!dateStr) return new Date(0);
  const s = dateStr.endsWith("Z") || /[+-]\d{2}:\d{2}$/.test(dateStr) ? dateStr : dateStr + "Z";
  return new Date(s);
}

export function formatDateTime(dateStr: string | null | undefined, locale = "ja-JP"): string {
  return parseUtcDate(dateStr).toLocaleString(locale);
}

export function formatTime(dateStr: string | null | undefined, locale = "ja-JP"): string {
  return parseUtcDate(dateStr).toLocaleTimeString(locale, { hour: "2-digit", minute: "2-digit" });
}

export function formatDateGroup(dateStr: string | null | undefined, locale = "ja-JP"): string {
  const target = parseUtcDate(dateStr);
  target.setHours(0, 0, 0, 0);
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const yesterday = new Date(today);
  yesterday.setDate(today.getDate() - 1);

  if (target.getTime() === today.getTime()) return "今日";
  if (target.getTime() === yesterday.getTime()) return "昨日";

  const diffDays = Math.floor((today.getTime() - target.getTime()) / 86400000);
  if (diffDays < 7) return `${diffDays}日前`;

  return target.toLocaleDateString(locale, { year: "numeric", month: "long", day: "numeric" });
}
