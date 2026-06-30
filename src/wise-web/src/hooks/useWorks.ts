import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { fetchWorks, API_BASE_URL } from '@/lib/api';
import { useGalleryStore } from '@/store/useGalleryStore';

function useHasActiveJobs() {
  const { data } = useQuery({
    queryKey: ['jobs', 'active'],
    queryFn: async () => {
      const res = await fetch(`${API_BASE_URL}/jobs/active`);
      return res.ok ? (res.json() as Promise<{ id: string }[]>) : [];
    },
    refetchInterval: 3000,
  });
  return (data?.length ?? 0) > 0;
}

export function useWorks() {
  const searchQuery = useGalleryStore((state) => state.searchQuery);
  const mediaTypeFilter = useGalleryStore((state) => state.mediaTypeFilter);
  const hasActiveJobs = useHasActiveJobs();

  return useInfiniteQuery({
    queryKey: ['works', searchQuery, mediaTypeFilter],
    queryFn: ({ pageParam = 1 }) => fetchWorks(pageParam as number, 50, searchQuery, mediaTypeFilter),
    getNextPageParam: (lastPage) => {
      const maxPages = Math.ceil(lastPage.totalCount / lastPage.pageSize);
      return lastPage.page < maxPages ? lastPage.page + 1 : undefined;
    },
    initialPageParam: 1,
    refetchInterval: hasActiveJobs ? 3000 : false,
  });
}
