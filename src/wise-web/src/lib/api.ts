export const API_BASE_URL = 'http://localhost:5162/api';

export interface WorkItem {
  id: string;
  primaryIdentifier: string | null;
  title: string | null;
  coverAssetId: string | null;
}

export interface WorksResponse {
  totalCount: number;
  page: number;
  pageSize: number;
  items: WorkItem[];
}

export async function fetchWorks(pageParam = 1, pageSize = 50, query = ''): Promise<WorksResponse> {
  const url = new URL(`${API_BASE_URL}/works`);
  url.searchParams.append('page', pageParam.toString());
  url.searchParams.append('pageSize', pageSize.toString());
  
  if (query) {
      url.searchParams.append('q', query);
  }

  const res = await fetch(url.toString());
  if (!res.ok) {
    throw new Error('Failed to fetch works');
  }
  return res.json();
}

export interface WorkDetail {
  id: string;
  primaryIdentifier: string | null;
  metadata: Array<{ fieldName: string, value: string, isPrimary: boolean, providerId: string, confidenceScore: number }>;
  assets: Array<{ id: string, originalFilename: string, fileSize: number, sha256: string | null }>;
  history: Array<{ eventType: string, occurredAt: string, actor: string, payload: string }>;
  diagnostic: {
    identifierConfidence: number,
    evidences: Array<{ providerId: string, score: number, rawValue: string }>
  };
}

export async function fetchWorkDetail(id: string): Promise<WorkDetail> {
  const res = await fetch(`${API_BASE_URL}/works/${id}`);
  if (!res.ok) {
    throw new Error('Failed to fetch work detail');
  }
  return res.json();
}

export function getAssetUrl(assetId: string | null): string {
  if (!assetId) return '';
  return `${API_BASE_URL}/assets/${assetId}/content`;
}
