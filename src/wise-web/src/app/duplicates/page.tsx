"use client";

import { fetchDuplicates, resolveDuplicate, DuplicateGroup, DuplicateWork } from "@/lib/api";
import { ArrowLeft, Copy, CheckCircle, AlertTriangle } from "lucide-react";
import Link from "next/link";
import { useEffect, useState, useCallback } from "react";

function formatFileSize(bytes: number): string {
  if (bytes === 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  return `${(bytes / Math.pow(1024, i)).toFixed(1)} ${units[i]}`;
}

function getVideoAsset(work: DuplicateWork) {
  return work.assets.find(
    (a) => a.assetType === "Video" || a.originalFilename.match(/\.(mp4|mkv|avi|mov)$/i)
  );
}

function getTotalVideoSize(work: DuplicateWork): number {
  const vid = getVideoAsset(work);
  return vid?.fileSize ?? 0;
}

interface GroupState {
  keepWorkId: string | null;
  deleteFiles: boolean;
  mergeRating: boolean;
  mergeMemo: boolean;
  mergeUserTags: boolean;
  mergeFavorite: boolean;
  resolving: boolean;
  resolved: boolean;
  error: string | null;
}

function hasUserData(work: DuplicateWork): boolean {
  return work.favorite || work.rating !== null || (work.userMemo !== null && work.userMemo.trim() !== "");
}

export default function DuplicatesPage() {
  const [groups, setGroups] = useState<DuplicateGroup[]>([]);
  const [loading, setLoading] = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);
  const [groupStates, setGroupStates] = useState<Record<string, GroupState>>({});

  const loadDuplicates = useCallback(async () => {
    setLoading(true);
    setFetchError(null);
    try {
      const data = await fetchDuplicates();
      setGroups(data);
      const states: Record<string, GroupState> = {};
      for (const g of data) {
        // Default: select the work with the largest video file as the keeper
        const sorted = [...g.works].sort((a, b) => getTotalVideoSize(b) - getTotalVideoSize(a));
        const anyHasUserData = g.works.some(hasUserData);
        states[g.identifier] = {
          keepWorkId: sorted[0]?.id ?? null,
          deleteFiles: false,
          mergeRating: anyHasUserData,
          mergeMemo: anyHasUserData,
          mergeUserTags: anyHasUserData,
          mergeFavorite: anyHasUserData,
          resolving: false,
          resolved: false,
          error: null,
        };
      }
      setGroupStates(states);
    } catch (e) {
      setFetchError(e instanceof Error ? e.message : "Failed to load duplicates");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadDuplicates();
  }, [loadDuplicates]);

  function updateGroupState(identifier: string, patch: Partial<GroupState>) {
    setGroupStates((prev) => ({
      ...prev,
      [identifier]: { ...prev[identifier], ...patch },
    }));
  }

  async function handleResolve(group: DuplicateGroup) {
    const state = groupStates[group.identifier];
    if (!state?.keepWorkId) return;

    const deleteWorkIds = group.works
      .filter((w) => w.id !== state.keepWorkId)
      .map((w) => w.id);
    if (deleteWorkIds.length === 0) return;

    updateGroupState(group.identifier, { resolving: true, error: null });
    try {
      await resolveDuplicate({
        keepWorkId: state.keepWorkId,
        deleteWorkIds,
        deleteFiles: state.deleteFiles,
        mergeRating: state.mergeRating,
        mergeMemo: state.mergeMemo,
        mergeUserTags: state.mergeUserTags,
        mergeFavorite: state.mergeFavorite,
      });
      updateGroupState(group.identifier, { resolving: false, resolved: true });
    } catch (e) {
      updateGroupState(group.identifier, {
        resolving: false,
        error: e instanceof Error ? e.message : "Failed to resolve",
      });
    }
  }

  const visibleGroups = groups.filter((g) => !groupStates[g.identifier]?.resolved);

  return (
    <main className="flex min-h-screen flex-col bg-background">
      <header className="sticky top-0 z-50 w-full border-b bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/60">
        <div className="container flex h-16 items-center px-4 md:px-6 gap-4">
          <Link
            href="/"
            className="p-2 rounded-full text-muted-foreground hover:text-foreground hover:bg-muted/50 transition-colors"
          >
            <ArrowLeft className="w-5 h-5" />
          </Link>
          <div className="flex items-center gap-2">
            <Copy className="w-5 h-5 text-primary" />
            <span className="font-bold text-xl tracking-wide">重複作品</span>
            {!loading && (
              <span className="text-sm text-muted-foreground ml-2">
                {visibleGroups.length > 0
                  ? `${visibleGroups.length} 組`
                  : "重複なし"}
              </span>
            )}
          </div>
        </div>
      </header>

      <div className="container px-4 md:px-6 py-6 max-w-4xl mx-auto w-full">
        {loading && (
          <div className="flex items-center justify-center py-20 text-muted-foreground">
            <span className="animate-pulse">読み込み中...</span>
          </div>
        )}

        {fetchError && (
          <div className="flex items-center gap-2 p-4 rounded-lg bg-destructive/10 text-destructive">
            <AlertTriangle className="w-5 h-5 flex-shrink-0" />
            <span>{fetchError}</span>
          </div>
        )}

        {!loading && !fetchError && visibleGroups.length === 0 && (
          <div className="flex flex-col items-center justify-center py-20 text-muted-foreground gap-3">
            <CheckCircle className="w-12 h-12 text-green-500" />
            <p className="text-lg">重複作品はありません</p>
          </div>
        )}

        <div className="flex flex-col gap-6">
          {visibleGroups.map((group) => {
            const state = groupStates[group.identifier];
            if (!state) return null;

            const sizes = group.works.map(getTotalVideoSize);
            const maxSize = Math.max(...sizes);
            const anyUserData = group.works.some(hasUserData);

            return (
              <div
                key={group.identifier}
                className="border rounded-xl p-5 bg-card shadow-sm flex flex-col gap-4"
              >
                {/* Header */}
                <div className="flex items-center gap-2 border-b pb-3 flex-wrap">
                  <span className="font-mono font-bold text-primary text-base truncate max-w-lg" title={group.identifier}>
                    {group.identifier.length > 80 ? group.identifier.slice(0, 80) + "…" : group.identifier}
                  </span>
                  <span className="text-xs text-muted-foreground shrink-0">
                    {group.works.length} 件の重複
                  </span>
                  <span className={`text-xs px-2 py-0.5 rounded-full font-medium shrink-0 ${
                    group.detectionType === "identifier"
                      ? "bg-blue-500/15 text-blue-600 dark:text-blue-400"
                      : "bg-violet-500/15 text-violet-600 dark:text-violet-400"
                  }`}>
                    {group.detectionType === "identifier" ? "品番一致" : "タイトル類似"}
                  </span>
                </div>

                {/* Work list */}
                <div className="flex flex-col gap-3">
                  {group.works.map((work) => {
                    const vid = getVideoAsset(work);
                    const size = getTotalVideoSize(work);
                    const isKeep = state.keepWorkId === work.id;
                    const isHighQuality = size === maxSize && maxSize > 0;

                    return (
                      <label
                        key={work.id}
                        className={`flex items-start gap-3 p-3 rounded-lg cursor-pointer border transition-colors ${
                          isKeep
                            ? "border-primary bg-primary/5"
                            : "border-border hover:border-muted-foreground/40"
                        }`}
                      >
                        <input
                          type="radio"
                          name={`keep-${group.identifier}`}
                          value={work.id}
                          checked={isKeep}
                          onChange={() =>
                            updateGroupState(group.identifier, { keepWorkId: work.id })
                          }
                          className="mt-1 accent-primary"
                        />
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2 flex-wrap">
                            <span className="font-medium text-sm truncate">
                              {vid?.originalFilename ?? work.primaryIdentifier}
                            </span>
                            {size > 0 && (
                              <span className="text-xs text-muted-foreground font-mono whitespace-nowrap">
                                {formatFileSize(size)}
                              </span>
                            )}
                            {isHighQuality && size > 0 && (
                              <span className="text-xs bg-green-500/20 text-green-600 dark:text-green-400 px-2 py-0.5 rounded-full font-medium whitespace-nowrap">
                                高品質
                              </span>
                            )}
                          </div>
                          <div className="flex items-center gap-3 mt-1 flex-wrap">
                            {work.rating !== null && (
                              <span className="text-xs text-amber-500">
                                {"★".repeat(work.rating)}{"☆".repeat(5 - work.rating)} ({work.rating}/5)
                              </span>
                            )}
                            {work.userMemo && (
                              <span className="text-xs text-muted-foreground italic truncate max-w-xs">
                                {work.userMemo}
                              </span>
                            )}
                            {work.status && (
                              <span className="text-xs text-muted-foreground">
                                {work.status}
                              </span>
                            )}
                          </div>
                        </div>
                        {isKeep && (
                          <span className="text-xs text-primary font-semibold whitespace-nowrap self-center">
                            残す
                          </span>
                        )}
                      </label>
                    );
                  })}
                </div>

                {/* Merge options */}
                {anyUserData && (
                  <div className="flex flex-wrap gap-4 text-sm pt-1 border-t">
                    <label className="flex items-center gap-2 cursor-pointer select-none">
                      <input
                        type="checkbox"
                        checked={state.mergeFavorite}
                        onChange={(e) =>
                          updateGroupState(group.identifier, { mergeFavorite: e.target.checked })
                        }
                        className="accent-primary"
                      />
                      <span className="text-muted-foreground">お気に入りをマージ</span>
                    </label>
                    <label className="flex items-center gap-2 cursor-pointer select-none">
                      <input
                        type="checkbox"
                        checked={state.mergeRating}
                        onChange={(e) =>
                          updateGroupState(group.identifier, { mergeRating: e.target.checked })
                        }
                        className="accent-primary"
                      />
                      <span className="text-muted-foreground">評価をマージ</span>
                    </label>
                    <label className="flex items-center gap-2 cursor-pointer select-none">
                      <input
                        type="checkbox"
                        checked={state.mergeMemo}
                        onChange={(e) =>
                          updateGroupState(group.identifier, { mergeMemo: e.target.checked })
                        }
                        className="accent-primary"
                      />
                      <span className="text-muted-foreground">メモをマージ</span>
                    </label>
                    <label className="flex items-center gap-2 cursor-pointer select-none">
                      <input
                        type="checkbox"
                        checked={state.mergeUserTags}
                        onChange={(e) =>
                          updateGroupState(group.identifier, { mergeUserTags: e.target.checked })
                        }
                        className="accent-primary"
                      />
                      <span className="text-muted-foreground">ユーザータグをマージ</span>
                    </label>
                  </div>
                )}

                {/* Delete files + resolve */}
                <div className="flex items-center gap-4 flex-wrap pt-1">
                  <label className="flex items-center gap-2 cursor-pointer select-none text-sm">
                    <input
                      type="checkbox"
                      checked={state.deleteFiles}
                      onChange={(e) =>
                        updateGroupState(group.identifier, { deleteFiles: e.target.checked })
                      }
                      className="accent-destructive"
                    />
                    <span className="text-destructive">削除時にファイルも削除</span>
                  </label>

                  <div className="ml-auto flex items-center gap-3">
                    {state.error && (
                      <span className="text-xs text-destructive">{state.error}</span>
                    )}
                    <button
                      onClick={() => handleResolve(group)}
                      disabled={state.resolving || !state.keepWorkId}
                      className="px-4 py-2 text-sm font-semibold rounded-lg bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                    >
                      {state.resolving ? "処理中..." : "解決する"}
                    </button>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </main>
  );
}
