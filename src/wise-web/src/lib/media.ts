export const API_ORIGIN = 'http://localhost:5162';

/** Resolve a cover URL (remote http or /api/ path) to an absolute URL usable by <Image>. */
export function resolveCoverUrl(url: string | null | undefined): string | null {
  if (!url) return null;
  if (url.startsWith('http')) return url;
  if (url.startsWith('/api/')) return `${API_ORIGIN}${url}`;
  return null;
}
