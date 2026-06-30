import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { MediaType, SortOption } from '@/lib/api';

export type Density = 'compact' | 'normal' | 'rich' | 'list';
export type CoverLayout = 'portrait' | 'landscape';
export type ListSortKey =
  | 'identifier' | 'title' | 'actress' | 'maker' | 'label' | 'releaseDate' | null;

export type Language = 'ja' | 'en';

export const DISPLAY_FIELD_LABELS = {
  identifier:  '品番',
  title:       'タイトル',
  actress:     '出演者',
  maker:       'メーカー',
  label:       'レーベル',
  releaseDate: '発売日',
  favorite:    'お気に入り',
  rating:      '評価',
  status:      'ステータス',
} as const;

export type DisplayField = keyof typeof DISPLAY_FIELD_LABELS;

export const DEFAULT_DISPLAY: Record<DisplayField, boolean> = {
  identifier:  true,
  title:       true,
  actress:     true,
  maker:       false,
  label:       false,
  releaseDate: false,
  favorite:    true,
  rating:      false,
  status:      false,
};

export { type SortOption };

interface GalleryState {
  density: Density;
  setDensity: (d: Density) => void;
  theme: 'light' | 'dark' | 'system';
  setTheme: (t: 'light' | 'dark' | 'system') => void;
  searchQuery: string;
  setSearchQuery: (q: string) => void;
  coverLayout: CoverLayout;
  setCoverLayout: (l: CoverLayout) => void;
  listSortKey: ListSortKey;
  listSortAsc: boolean;
  setListSort: (key: ListSortKey, asc: boolean) => void;
  sort: SortOption;
  setSort: (s: SortOption) => void;
  language: Language;
  setLanguage: (l: Language) => void;
  displayFields: Record<DisplayField, boolean>;
  setDisplayField: (field: DisplayField, value: boolean) => void;
  resetDisplayFields: () => void;
  mediaTypeFilter: MediaType | null;
  setMediaTypeFilter: (mt: MediaType | null) => void;
}

export const useGalleryStore = create<GalleryState>()(
  persist(
    (set) => ({
      density: 'normal',
      setDensity: (density) => set({ density }),
      theme: 'system',
      setTheme: (theme) => set({ theme }),
      searchQuery: '',
      setSearchQuery: (searchQuery) => set({ searchQuery }),
      coverLayout: 'portrait',
      setCoverLayout: (coverLayout) => set({ coverLayout }),
      listSortKey: 'identifier',
      listSortAsc: true,
      setListSort: (key, asc) => set({ listSortKey: key, listSortAsc: asc }),
      sort: 'added',
      setSort: (sort) => set({ sort }),
      language: 'ja',
      setLanguage: (language) => set({ language }),
      displayFields: { ...DEFAULT_DISPLAY },
      setDisplayField: (field, value) =>
        set((s) => ({ displayFields: { ...s.displayFields, [field]: value } })),
      resetDisplayFields: () => set({ displayFields: { ...DEFAULT_DISPLAY } }),
      mediaTypeFilter: null,
      setMediaTypeFilter: (mediaTypeFilter) => set({ mediaTypeFilter }),
    }),
    // New storage key — avoids conflicts with v1 persisted state
    { name: 'wise-gallery-v2' }
  )
);
