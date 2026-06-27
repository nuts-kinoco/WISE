import { create } from 'zustand';
import { persist } from 'zustand/middleware';

type CardSize = 'sm' | 'md' | 'lg';
type Theme = 'light' | 'dark' | 'system';

interface GalleryState {
  cardSize: CardSize;
  setCardSize: (size: CardSize) => void;
  theme: Theme;
  setTheme: (theme: Theme) => void;
  searchQuery: string;
  setSearchQuery: (query: string) => void;
}

export const useGalleryStore = create<GalleryState>()(
  persist(
    (set) => ({
      cardSize: 'md',
      setCardSize: (size) => set({ cardSize: size }),
      theme: 'system',
      setTheme: (theme) => set({ theme }),
      searchQuery: '',
      setSearchQuery: (query) => set({ searchQuery: query }),
    }),
    {
      name: 'wise-gallery-storage',
    }
  )
);
