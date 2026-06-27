import { useQuery } from '@tanstack/react-query';
import { fetchWorkDetail } from '@/lib/api';

export function useWork(id: string) {
  return useQuery({
    queryKey: ['work', id],
    queryFn: () => fetchWorkDetail(id),
    enabled: !!id,
  });
}
