export const API_BASE_URL = 'http://localhost:5162/api';

export type MediaType = 'Video' | 'Comic' | 'Book' | 'PhotoBook' | 'ImageCollection' | 'Audio';

export interface WorkItem {
  id: string;
  primaryIdentifier: string | null;
  mediaType: MediaType;
  title: string | null;
  coverUrl: string | null;
  coverLandscapeUrl: string | null;
  // Video fields
  actress: string | null;
  maker: string | null;
  label: string | null;
  // Comic/Book fields
  author: string | null;
  circle: string | null;
  pageCount: string | null;
  language: string | null;
  // Shared
  releaseDate: string | null;
  metadataStatus: string;
  favorite: boolean;
  rating: number | null;
}

export interface WorksResponse {
  totalCount: number;
  page: number;
  pageSize: number;
  items: WorkItem[];
}

export async function fetchWorks(
  pageParam = 1,
  pageSize = 50,
  query = '',
  mediaType?: MediaType | null,
  status?: string,
): Promise<WorksResponse> {
  const url = new URL(`${API_BASE_URL}/works`);
  url.searchParams.append('page', pageParam.toString());
  url.searchParams.append('pageSize', pageSize.toString());
  if (query) url.searchParams.append('q', query);
  if (mediaType) url.searchParams.append('mediaType', mediaType);
  if (status) url.searchParams.append('status', status);

  const res = await fetch(url.toString());
  if (!res.ok) throw new Error('Failed to fetch works');
  return res.json();
}

// ── Display Profile API ────────────────────────────────────────────────────

export interface DisplayProfileField {
  fieldName: string;
  label: string;
  isVisible: boolean;
  displayOrder: number;
}

export interface DisplayProfile {
  mediaType: MediaType;
  coverOrientation: 'portrait' | 'landscape';
  defaultSort: string;
  isUserCustomized: boolean;
  fields: DisplayProfileField[];
}

export async function fetchDisplayProfiles(): Promise<DisplayProfile[]> {
  const res = await fetch(`${API_BASE_URL}/settings/display-profiles`);
  if (!res.ok) throw new Error('Failed to fetch display profiles');
  return res.json();
}

export async function fetchDisplayProfile(mediaType: MediaType): Promise<DisplayProfile> {
  const res = await fetch(`${API_BASE_URL}/settings/display-profiles/${mediaType}`);
  if (!res.ok) throw new Error(`Failed to fetch display profile for ${mediaType}`);
  return res.json();
}

export async function patchDisplayProfile(
  mediaType: MediaType,
  data: Partial<Pick<DisplayProfile, 'coverOrientation'>> & { fields?: Partial<DisplayProfileField>[] }
): Promise<DisplayProfile> {
  const res = await fetch(`${API_BASE_URL}/settings/display-profiles/${mediaType}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data),
  });
  if (!res.ok) throw new Error('Failed to update display profile');
  return res.json();
}

// ── Reader API ─────────────────────────────────────────────────────────────

export interface ReaderPage {
  index: number;
  fileName: string;
  contentType: string;
}

export interface ReaderInfo {
  workId: string;
  assetId: string;
  storageFormat: string;
  totalPages: number;
  pages: ReaderPage[];
}

export async function fetchReaderPages(workId: string): Promise<ReaderInfo> {
  const res = await fetch(`${API_BASE_URL}/works/${workId}/reader/pages`);
  if (!res.ok) throw new Error('Failed to fetch reader pages');
  return res.json();
}

export function getReaderPageUrl(workId: string, pageIndex: number): string {
  return `${API_BASE_URL}/works/${workId}/reader/pages/${pageIndex}`;
}

export function getEpubUrl(workId: string): string {
  return `${API_BASE_URL}/works/${workId}/epub`;
}

// ── Viewer Info API ────────────────────────────────────────────────────────

export interface ViewerInfo {
  viewerType: string;
  route: string | null;
  capabilities: {
    supportsPageNavigation: boolean;
    supportsDoublePage: boolean;
    supportsPrefetch: boolean;
    supportsTimeSeek: boolean;
    supportsResume: boolean;
  } | null;
}

export async function fetchViewerInfo(workId: string): Promise<ViewerInfo> {
  const res = await fetch(`${API_BASE_URL}/works/${workId}/viewer-info`);
  if (!res.ok) throw new Error('Failed to fetch viewer info');
  return res.json();
}

export interface WorkDetail {
  id: string;
  primaryIdentifier: string | null;
  favorite: boolean;
  rating: number | null;
  userMemo: string | null;
  sampleImages: string[];
  metadata: Array<{ fieldName: string, value: string, isPrimary: boolean, providerId: string, confidenceScore: number }>;
  assets: Array<{ id: string, originalFilename: string, fileSize: number, sha256: string | null, assetType: string }>;
  history: Array<{ eventType: string, occurredAt: string, actor: string, payload: string }>;
  diagnostic: {
    confidence: number,
    decision: string,
    rejectReason: string | null,
    identifier: string | null,
    candidates: Array<{ pattern: string, value: string, provider: string }>,
    evidences: Array<{ type: string, value: string, score: number, provider: string }>
  } | null;
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

export async function patchUserData(id: string, data: { favorite?: boolean; rating?: number | null; memo?: string }): Promise<void> {
  const res = await fetch(`${API_BASE_URL}/works/${id}/user-data`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data),
  });
  if (!res.ok) throw new Error('Failed to update user data');
}

export interface AppSetting {
  key: string;
  value: string;
}

export async function fetchSettings(): Promise<AppSetting[]> {
  const res = await fetch(`${API_BASE_URL}/settings`);
  if (!res.ok) throw new Error('Failed to fetch settings');
  return res.json();
}

export async function patchSetting(key: string, value: string): Promise<void> {
  const res = await fetch(`${API_BASE_URL}/settings/${key}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ value }),
  });
  if (!res.ok) throw new Error('Failed to update setting');
}

export interface MetadataProvider {
  id: string;
  priority: number;
}

export async function fetchProviders(): Promise<MetadataProvider[]> {
  const res = await fetch(`${API_BASE_URL}/jobs/providers`);
  if (!res.ok) throw new Error('Failed to fetch providers');
  return res.json();
}

export async function enqueueFetchMetadata(workId: string): Promise<{ jobId: string }> {
  const res = await fetch(`${API_BASE_URL}/jobs/fetchmetadata`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ workId }),
  });
  if (!res.ok) throw new Error('Failed to queue metadata job');
  return res.json();
}

export async function enqueueFetchMetadataBatch(workIds: string[]): Promise<{ queued: number }> {
  const res = await fetch(`${API_BASE_URL}/jobs/fetchmetadata/batch`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ workIds }),
  });
  if (!res.ok) throw new Error('Failed to queue batch metadata jobs');
  return res.json();
}

export async function openSystemPath(path: string): Promise<void> {
  await fetch(`${API_BASE_URL}/system/open-path`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ path }),
  });
}

export interface ThumbnailAsset {
  id: string;
  originalFilename: string;
  assetType: string;
  url: string;
  isCurrentCover: boolean;
}

export async function fetchThumbnailAssets(workId: string): Promise<ThumbnailAsset[]> {
  const res = await fetch(`${API_BASE_URL}/works/${workId}/thumbnail-assets`);
  if (!res.ok) throw new Error('Failed to fetch thumbnail assets');
  return res.json();
}

export async function setCover(workId: string, assetId: string): Promise<{ coverUrl: string }> {
  const res = await fetch(`${API_BASE_URL}/works/${workId}/set-cover`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ assetId }),
  });
  if (!res.ok) throw new Error('Failed to set cover');
  return res.json();
}

export async function uploadCover(workId: string, file: File): Promise<{ assetId: string; url: string }> {
  const form = new FormData();
  form.append('file', file);
  const res = await fetch(`${API_BASE_URL}/works/${workId}/upload-cover`, {
    method: 'POST',
    body: form,
  });
  if (!res.ok) throw new Error('Failed to upload cover');
  return res.json();
}

export async function openFolder(id: string): Promise<{ path: string }> {
  const res = await fetch(`${API_BASE_URL}/works/${id}/open-folder`, { method: 'POST' });
  if (!res.ok) throw new Error('Failed to open folder');
  return res.json();
}

export async function clearHistory(): Promise<{ deleted: number }> {
  const res = await fetch(`${API_BASE_URL}/system/history`, { method: 'DELETE' });
  if (!res.ok) throw new Error('Failed to clear history');
  return res.json();
}

export async function clearFinishedJobs(): Promise<{ deleted: number }> {
  const res = await fetch(`${API_BASE_URL}/jobs`, { method: 'DELETE' });
  if (!res.ok) throw new Error('Failed to clear jobs');
  return res.json();
}

export async function deleteWork(id: string, deleteFiles = false): Promise<void> {
  const res = await fetch(`${API_BASE_URL}/works/${id}?deleteFiles=${deleteFiles}`, {
    method: 'DELETE',
  });
  if (!res.ok) throw new Error('Failed to delete work');
}

export async function addUserTag(workId: string, value: string): Promise<void> {
  const res = await fetch(`${API_BASE_URL}/works/${workId}/user-tags`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ value }),
  });
  if (!res.ok) throw new Error('Failed to add tag');
}

export async function deleteUserTag(workId: string, tagValue: string): Promise<void> {
  const res = await fetch(`${API_BASE_URL}/works/${workId}/user-tags/${encodeURIComponent(tagValue)}`, {
    method: 'DELETE',
  });
  if (!res.ok) throw new Error('Failed to delete tag');
}

// ── Reading History API ────────────────────────────────────────────────────

export interface ReadingHistory {
  workId: string;
  deviceId: string;
  pageNumber: number | null;
  positionSeconds: number | null;
  positionPercent: number | null;
  lastReadAt: string;
  updatedAt: string;
}

export async function fetchReadingHistory(workId: string, deviceId: string): Promise<ReadingHistory | null> {
  const res = await fetch(
    `${API_BASE_URL}/works/${workId}/reading-history?deviceId=${encodeURIComponent(deviceId)}`
  );
  if (res.status === 404) return null;
  if (!res.ok) throw new Error('Failed to fetch reading history');
  return res.json();
}

export async function saveReadingHistory(
  workId: string,
  data: { deviceId: string; pageNumber?: number | null; positionSeconds?: number | null; positionPercent?: number | null }
): Promise<ReadingHistory> {
  const res = await fetch(`${API_BASE_URL}/works/${workId}/reading-history`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data),
  });
  if (!res.ok) throw new Error('Failed to save reading history');
  return res.json();
}

export async function deleteReadingHistory(workId: string, deviceId: string): Promise<void> {
  await fetch(
    `${API_BASE_URL}/works/${workId}/reading-history?deviceId=${encodeURIComponent(deviceId)}`,
    { method: 'DELETE' }
  );
}

export async function deleteGenreTag(workId: string, tagValue: string): Promise<void> {
  const res = await fetch(`${API_BASE_URL}/works/${workId}/genre-tags/${encodeURIComponent(tagValue)}`, {
    method: 'DELETE',
  });
  if (!res.ok) throw new Error('Failed to delete genre tag');
}

export interface DuplicateWork {
  id: string;
  primaryIdentifier: string;
  status: string;
  title: string | null;
  actress: string | null;
  maker: string | null;
  favorite: boolean;
  rating: number | null;
  userMemo: string | null;
  assets: Array<{ id: string; originalFilename: string; fileSize: number; assetType: string }>;
}

export interface DuplicateGroup {
  identifier: string;
  detectionType: 'identifier' | 'title';
  works: DuplicateWork[];
}

export async function fetchDuplicates(): Promise<DuplicateGroup[]> {
  const res = await fetch(`${API_BASE_URL}/duplicates`);
  if (!res.ok) throw new Error('Failed to fetch duplicates');
  return res.json();
}

export async function resolveDuplicate(params: {
  keepWorkId: string;
  deleteWorkIds: string[];   // 複数削除対応
  deleteFiles: boolean;
  mergeRating: boolean;
  mergeMemo: boolean;
  mergeUserTags: boolean;
}): Promise<void> {
  const res = await fetch(`${API_BASE_URL}/duplicates/resolve`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(params),
  });
  if (!res.ok) throw new Error('Failed to resolve duplicate');
}
