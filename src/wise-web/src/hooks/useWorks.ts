import { useInfiniteQuery } from '@tanstack/react-query';
import { fetchWorks } from '@/lib/api';
import { useGalleryStore } from '@/store/useGalleryStore';

export function useWorks() {
  const searchQuery = useGalleryStore((state) => state.searchQuery);

  return useInfiniteQuery({
    queryKey: ['works', searchQuery],
    queryFn: ({ pageParam = 1 }) => fetchWorks(pageParam as number, 50, searchQuery),
    getNextPageParam: (lastPage) => {
      const maxPages = Math.ceil(lastPage.totalCount / lastPage.pageSize);
      return lastPage.page < maxPages ? lastPage.page + 1 : undefined;
    },
    initialPageParam: 1,
  });
}
