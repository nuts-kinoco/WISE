"use client";

import { useWork } from "@/hooks/useWork";
import { deleteWork, patchUserData, enqueueFetchMetadata, openFolder, fetchThumbnailAssets, setCover, uploadCover, ThumbnailAsset, addUserTag, deleteUserTag, deleteGenreTag } from "@/lib/api";
import { resolveCoverUrl } from "@/lib/media";
import { useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Loader2, ChevronDown, ChevronUp, Calendar, Clock, Building2, Tag, Film, Layers, Trash2, Heart, Star, RefreshCw, FolderOpen, Upload, Pencil, X, Plus, CheckCircle2, AlertCircle, History, BookOpen } from "lucide-react";
import Image from "next/image";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { use, useState, useEffect, useRef } from "react";
import { useGalleryStore } from "@/store/useGalleryStore";

const API_ORIGIN = "http://localhost:5162";

const EVENT_LABELS: Record<string, string> = {
  "Work Created": "インポート完了",
  "Import Completed": "インポート完了",
  "Identifier Resolved": "識別子解決",
  "Identifier resolved": "識別子解決",
  "Metadata Fetched": "メタデータ取得完了",
  "Metadata fetched": "メタデータ取得完了",
  "Portrait Cover Downloaded": "ポートレートカバーダウンロード完了",
  "Landscape Cover Downloaded": "ランドスケープカバーダウンロード完了",
  "Thumbnail Generated": "サムネイル生成完了",
  "Thumbnail generated": "サムネイル生成完了",
  "Video Ready": "動画準備完了",
  "Video ready": "動画準備完了",
};

export default function WorkDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const resolvedParams = use(params);
  const { data: work, isLoading, isError } = useWork(resolvedParams.id);
  const [devMode, setDevMode] = useState(false);
  const [coverTab, setCoverTab] = useState<"portrait" | "landscape">("portrait");
  const [deleteModal, setDeleteModal] = useState(false);
  const [deleteFiles, setDeleteFiles] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [favorite, setFavorite] = useState<boolean | null>(null);
  const [rating, setRating] = useState<number | null>(null);
  const [memo, setMemo] = useState<string>("");
  const [sampleIdx, setSampleIdx] = useState(0);
  const [videoTabIdx, setVideoTabIdx] = useState(0);
  const [rescraping, setRescraping] = useState(false);
  const [rescrapeMsg, setRescrapeMsg] = useState<string | null>(null);
  const [openingFolder, setOpeningFolder] = useState(false);
  const [coverPickerOpen, setCoverPickerOpen] = useState(false);
  const [thumbnailAssets, setThumbnailAssets] = useState<ThumbnailAsset[]>([]);
  const [settingCover, setSettingCover] = useState<string | null>(null);
  const [isDraggingCover, setIsDraggingCover] = useState(false);
  const [uploadingCover, setUploadingCover] = useState(false);
  const [uploadMsg, setUploadMsg] = useState<string | null>(null);
  const [tagEditing, setTagEditing] = useState(false);
  const [tagInput, setTagInput] = useState("");
  const [tagDeleteConfirm, setTagDeleteConfirm] = useState<{ tag: string; isScraped: boolean } | null>(null);
  const router = useRouter();
  const queryClient = useQueryClient();
  const setSearchQuery = useGalleryStore((state) => state.setSearchQuery);

  useEffect(() => {
    if (work) {
      if (favorite === null) setFavorite(work.favorite ?? false);
      if (rating === null) setRating(work.rating ?? null);
      if (!memo && work.userMemo) setMemo(work.userMemo);
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [work?.id]);

  const handleFavorite = async () => {
    const next = !(favorite ?? false);
    setFavorite(next);
    try { await patchUserData(resolvedParams.id, { favorite: next }); } catch { setFavorite(!next); }
  };

  const handleRating = async (stars: number) => {
    const next = rating === stars ? null : stars;
    setRating(next);
    try { await patchUserData(resolvedParams.id, { rating: next }); } catch { setRating(rating); }
  };

  const handleMemoBlur = async () => {
    try { await patchUserData(resolvedParams.id, { memo }); } catch {}
  };

  const filterBy = (value: string) => {
    setSearchQuery(value);
    router.push("/");
  };

  const handleOpenCoverPicker = async () => {
    setCoverPickerOpen(true);
    try {
      const assets = await fetchThumbnailAssets(resolvedParams.id);
      setThumbnailAssets(assets);
    } catch { setThumbnailAssets([]); }
  };

  const handleSetCover = async (assetId: string) => {
    setSettingCover(assetId);
    try {
      await setCover(resolvedParams.id, assetId);
      await queryClient.invalidateQueries({ queryKey: ["work", resolvedParams.id] });
      setCoverPickerOpen(false);
    } catch { }
    finally { setSettingCover(null); }
  };

  const handleOpenFolder = async () => {
    if (!work || openingFolder) return;
    setOpeningFolder(true);
    try { await openFolder(work.id); } catch { }
    finally { setOpeningFolder(false); }
  };

  const handleRescrape = async () => {
    if (!work || rescraping) return;
    setRescraping(true);
    setRescrapeMsg(null);
    try {
      await enqueueFetchMetadata(work.id);
      setRescrapeMsg("再スキャンをキューに追加しました");
    } catch {
      setRescrapeMsg("エラーが発生しました");
    } finally {
      setRescraping(false);
      setTimeout(() => setRescrapeMsg(null), 3000);
    }
  };

  const handleCoverDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = "copy";
    setIsDraggingCover(true);
  };

  const handleCoverDragLeave = (e: React.DragEvent) => {
    if (!e.currentTarget.contains(e.relatedTarget as Node)) setIsDraggingCover(false);
  };

  const handleCoverDrop = async (e: React.DragEvent) => {
    e.preventDefault();
    setIsDraggingCover(false);
    if (!work) return;
    const file = Array.from(e.dataTransfer.files).find(f => f.type.startsWith("image/"));
    if (!file) { setUploadMsg("画像ファイルをドロップしてください"); setTimeout(() => setUploadMsg(null), 3000); return; }
    setUploadingCover(true);
    setUploadMsg(null);
    try {
      await uploadCover(work.id, file);
      await queryClient.invalidateQueries({ queryKey: ["work", work.id] });
      setUploadMsg("カバー画像を登録しました");
    } catch {
      setUploadMsg("アップロードに失敗しました");
    } finally {
      setUploadingCover(false);
      setTimeout(() => setUploadMsg(null), 3000);
    }
  };

  const handleDelete = async () => {
    if (!work) return;
    setDeleting(true);
    try {
      await deleteWork(work.id, deleteFiles);
      await queryClient.invalidateQueries({ queryKey: ["works"] });
      router.push("/");
    } catch {
      setDeleting(false);
      setDeleteModal(false);
    }
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-screen bg-background">
        <Loader2 className="w-12 h-12 animate-spin text-primary" />
      </div>
    );
  }

  if (isError || !work) {
    return (
      <div className="flex flex-col items-center justify-center min-h-screen bg-background text-destructive">
        <h1 className="text-3xl font-bold mb-4">作品が見つかりません</h1>
        <Link href="/" className="text-primary hover:underline flex items-center gap-2">
          <ArrowLeft className="w-4 h-4" /> ギャラリーに戻る
        </Link>
      </div>
    );
  }

  const getMeta = (name: string) =>
    work.metadata.find((m) => m.fieldName === name && m.isPrimary)?.value
    ?? work.metadata.find((m) => m.fieldName === name)?.value;
  const getMetaAll = (name: string) => {
    const primary = work.metadata.filter((m) => m.fieldName === name && m.isPrimary).map((m) => m.value);
    return primary.length > 0 ? primary : work.metadata.filter((m) => m.fieldName === name).map((m) => m.value);
  };

  const title = getMeta("Title");
  // 1名: Actress フィールド、複数: ActressTag フィールド（Actress は空文字マーカー）
  const actressSingle = getMetaAll("Actress").filter(a => a.trim() !== "");
  const actressTags = getMetaAll("ActressTag");
  const actress = actressTags.length > 0 ? actressTags : actressSingle;
  const maker = getMeta("Maker");
  const series = getMeta("Series");
  const label = getMeta("Label");
  const releaseDate = getMeta("ReleaseDate");
  const runtime = getMeta("Runtime");
  // Genres are stored as |-joined string (to avoid multi-value dedup in backend)
  const genres = getMetaAll("Genre").flatMap(g => g.split("|").map(s => s.trim()).filter(Boolean))
    || work.metadata.filter(m => m.fieldName === "Genre").flatMap(m => m.value.split("|").map(s => s.trim()).filter(Boolean));

  // Cover from PortraitCover / LandscapeCover MetadataFields
  const portraitCoverUrl = resolveCoverUrl(getMeta("PortraitCover") ?? getMeta("Cover"));
  const landscapeCoverUrl = resolveCoverUrl(getMeta("LandscapeCover") ?? getMeta("CoverLandscape"));
  const activeCoverUrl = coverTab === "landscape" && landscapeCoverUrl ? landscapeCoverUrl : portraitCoverUrl;

  // Completion status: title + portrait cover + identifier = isComplete
  const isComplete = !!(title && portraitCoverUrl && work.primaryIdentifier);

  // Archive assets (comic/book reader)
  const archiveAssets = work.assets.filter((a) =>
    a.originalFilename?.match(/\.(zip|cbz|rar|cbr|pdf)$/i) || a.assetType === "Archive"
  );
  const hasReader = archiveAssets.length > 0;

  // Video assets (multiple for FC2 serial files)
  const videoAssets = work.assets
    .filter((a) => a.assetType === "Video" || a.originalFilename?.match(/\.(mp4|mkv)$/i))
    .sort((a, b) => a.originalFilename.localeCompare(b.originalFilename));
  const activeVideoAsset = videoAssets[videoTabIdx] ?? videoAssets[0];
  const videoSrc = activeVideoAsset ? `${API_ORIGIN}/api/assets/${activeVideoAsset.id}/content` : null;

  // Extract a short label from filename: "FC2-PPV-4409072-01.mp4" → "01"
  const getVideoLabel = (filename: string) => {
    const m = filename.match(/-(\d{2})(?:_[^.]+)?\.\w+$/);
    return m ? m[1] : filename.replace(/\.[^.]+$/, "");
  };

  return (
    <main className="min-h-screen bg-background text-foreground">
      {/* Hero Background */}
      {activeCoverUrl && (
        <div className="fixed top-0 left-0 w-full h-[50vh] z-0 overflow-hidden pointer-events-none">
          <Image
            src={activeCoverUrl}
            alt=""
            fill
            className="object-cover opacity-20 blur-2xl scale-110"
            unoptimized
            priority
          />
          <div className="absolute inset-0 bg-gradient-to-b from-background/40 via-background/70 to-background" />
        </div>
      )}

      <div className="relative z-10">
        {/* Nav */}
        <div className="container mx-auto px-4 md:px-8 pt-6 pb-2 flex items-center justify-between">
          <Link
            href="/"
            className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground transition-colors"
          >
            <ArrowLeft className="w-4 h-4" /> ギャラリーに戻る
          </Link>
          <div className="flex items-center gap-2">
            {hasReader && (
              <Link
                href={`/works/${resolvedParams.id}/reader`}
                className="inline-flex items-center gap-1.5 text-sm font-medium text-white bg-primary hover:bg-primary/90 transition-colors px-4 py-1.5 rounded-lg"
              >
                <BookOpen className="w-4 h-4" /> 読む
              </Link>
            )}
            {rescrapeMsg && (
              <span className="text-xs text-muted-foreground">{rescrapeMsg}</span>
            )}
            <button
              onClick={handleOpenFolder}
              disabled={openingFolder}
              className="inline-flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors px-3 py-1.5 rounded-lg hover:bg-muted/50 border border-transparent hover:border-border/50"
            >
              <FolderOpen className="w-3.5 h-3.5" /> ファイルの場所
            </button>
            <button
              onClick={handleRescrape}
              disabled={rescraping}
              className="inline-flex items-center gap-1.5 text-xs text-muted-foreground hover:text-primary transition-colors px-3 py-1.5 rounded-lg hover:bg-primary/10 border border-transparent hover:border-primary/20"
            >
              <RefreshCw className={`w-3.5 h-3.5 ${rescraping ? "animate-spin" : ""}`} /> 再スキャン
            </button>
            <button
              onClick={() => setDeleteModal(true)}
              className="inline-flex items-center gap-1.5 text-xs text-muted-foreground hover:text-destructive transition-colors px-3 py-1.5 rounded-lg hover:bg-destructive/10 border border-transparent hover:border-destructive/20"
            >
              <Trash2 className="w-3.5 h-3.5" /> 削除
            </button>
          </div>
        </div>

        {/* Cover Picker Modal */}
        {coverPickerOpen && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm" onClick={() => setCoverPickerOpen(false)}>
            <div className="bg-card border border-border rounded-2xl p-5 w-full max-w-2xl mx-4 shadow-2xl max-h-[90vh] flex flex-col" onClick={e => e.stopPropagation()}>
              <div className="flex items-center justify-between mb-4 shrink-0">
                <h2 className="text-base font-bold">カバー画像を選択</h2>
                <button onClick={() => setCoverPickerOpen(false)} className="text-muted-foreground hover:text-foreground p-1 text-lg leading-none">✕</button>
              </div>
              {thumbnailAssets.length === 0 ? (
                <p className="text-sm text-muted-foreground text-center py-8">画像が見つかりません</p>
              ) : (
                <div className="overflow-y-auto space-y-5">
                  {/* Uploaded covers (PortraitCover with no original — from upload) */}
                  {/* Package covers */}
                  {thumbnailAssets.some(a => a.assetType === "PortraitCover" || a.assetType === "LandscapeCover") && (
                    <div>
                      <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider mb-2">パッケージ画像</p>
                      <div className="flex flex-wrap gap-2">
                        {thumbnailAssets.filter(a => a.assetType === "PortraitCover" || a.assetType === "LandscapeCover").map(a => (
                          <button
                            key={a.id}
                            onClick={() => handleSetCover(a.id)}
                            disabled={!!settingCover}
                            className={`relative rounded-lg overflow-hidden border-2 transition-all group ${a.isCurrentCover ? "border-primary ring-2 ring-primary/40" : "border-border hover:border-primary"}`}
                            style={{ width: a.assetType === "LandscapeCover" ? 160 : 90, height: a.assetType === "LandscapeCover" ? 90 : 135 }}
                          >
                            <Image src={`http://localhost:5162${a.url}`} alt={a.originalFilename} fill className="object-cover" unoptimized />
                            {settingCover === a.id && <div className="absolute inset-0 bg-black/50 flex items-center justify-center"><Loader2 className="w-5 h-5 animate-spin text-white" /></div>}
                            <div className="absolute top-1 left-1">
                              <span className="text-[9px] bg-black/70 text-white px-1 py-0.5 rounded">{a.assetType === "LandscapeCover" ? "横" : "縦"}</span>
                            </div>
                            {a.isCurrentCover && <div className="absolute bottom-1 right-1 w-4 h-4 bg-primary rounded-full flex items-center justify-center text-[10px] text-white font-bold">✓</div>}
                          </button>
                        ))}
                      </div>
                    </div>
                  )}
                  {/* Thumbnails & samples */}
                  {thumbnailAssets.some(a => a.assetType === "Thumbnail" || a.assetType === "SampleImage") && (
                    <div>
                      <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider mb-2">サムネイル / サンプル画像</p>
                      <div className="grid grid-cols-4 gap-2">
                        {thumbnailAssets.filter(a => a.assetType === "Thumbnail" || a.assetType === "SampleImage").map(a => (
                          <button
                            key={a.id}
                            onClick={() => handleSetCover(a.id)}
                            disabled={!!settingCover}
                            className={`relative aspect-[2/3] rounded-lg overflow-hidden border-2 transition-all group ${a.isCurrentCover ? "border-primary ring-2 ring-primary/40" : "border-border hover:border-primary"}`}
                          >
                            <Image src={`http://localhost:5162${a.url}`} alt={a.originalFilename} fill className="object-cover" unoptimized />
                            {settingCover === a.id && <div className="absolute inset-0 bg-black/50 flex items-center justify-center"><Loader2 className="w-5 h-5 animate-spin text-white" /></div>}
                            {a.isCurrentCover && <div className="absolute top-1 right-1 w-4 h-4 bg-primary rounded-full flex items-center justify-center text-[10px] text-white font-bold">✓</div>}
                            <div className="absolute bottom-0 left-0 right-0 bg-black/60 px-1 py-0.5 text-[10px] text-white truncate opacity-0 group-hover:opacity-100 transition-opacity">{a.originalFilename}</div>
                          </button>
                        ))}
                      </div>
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>
        )}

        {/* Delete Modal */}
        {deleteModal && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
            <div className="bg-card border border-border rounded-2xl p-6 w-full max-w-sm mx-4 shadow-2xl">
              <h2 className="text-lg font-bold mb-1">作品を削除しますか？</h2>
              <p className="text-sm text-muted-foreground mb-5">
                <span className="font-mono text-primary">{work?.primaryIdentifier}</span> をライブラリから削除します。この操作は取り消せません。
              </p>
              <label className="flex items-center gap-2.5 mb-5 cursor-pointer select-none">
                <input
                  type="checkbox"
                  checked={deleteFiles}
                  onChange={(e) => setDeleteFiles(e.target.checked)}
                  className="w-4 h-4 accent-destructive"
                />
                <span className="text-sm">動画・カバー画像などの物理ファイルも削除する</span>
              </label>
              <div className="flex gap-3">
                <button
                  onClick={() => setDeleteModal(false)}
                  disabled={deleting}
                  className="flex-1 py-2.5 rounded-xl border border-border text-sm hover:bg-muted/50 transition-colors disabled:opacity-50"
                >
                  キャンセル
                </button>
                <button
                  onClick={handleDelete}
                  disabled={deleting}
                  className="flex-1 py-2.5 rounded-xl bg-destructive text-destructive-foreground text-sm font-bold hover:bg-destructive/90 transition-colors disabled:opacity-50 flex items-center justify-center gap-2"
                >
                  {deleting ? <Loader2 className="w-4 h-4 animate-spin" /> : <Trash2 className="w-4 h-4" />}
                  {deleting ? "削除中..." : "削除する"}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Main Content */}
        <div className="container mx-auto px-4 md:px-8 py-4">
          <div className="flex flex-col lg:flex-row gap-8 lg:gap-12">

            {/* Left — Cover + Play */}
            <div className="w-full lg:w-72 xl:w-80 shrink-0 flex flex-col gap-3">
              {portraitCoverUrl && landscapeCoverUrl && (
                <div className="flex bg-muted/50 rounded-full p-0.5 self-start">
                  <button
                    onClick={() => setCoverTab("portrait")}
                    className={`px-3 py-1 rounded-full text-xs font-medium transition-colors ${coverTab === "portrait" ? "bg-background shadow text-foreground" : "text-muted-foreground"}`}
                  >
                    縦
                  </button>
                  <button
                    onClick={() => setCoverTab("landscape")}
                    className={`px-3 py-1 rounded-full text-xs font-medium transition-colors ${coverTab === "landscape" ? "bg-background shadow text-foreground" : "text-muted-foreground"}`}
                  >
                    横
                  </button>
                </div>
              )}

              <div
                className={`relative w-full rounded-2xl overflow-hidden shadow-2xl ring-1 ring-white/10 bg-muted ${
                  coverTab === "landscape" ? "aspect-video" : "aspect-[2/3]"
                } ${isDraggingCover ? "ring-2 ring-primary ring-offset-2" : ""}`}
                onDragOver={handleCoverDragOver}
                onDragLeave={handleCoverDragLeave}
                onDrop={handleCoverDrop}
              >
                {activeCoverUrl ? (
                  <Image
                    src={activeCoverUrl}
                    alt={title ?? work.primaryIdentifier ?? ""}
                    fill
                    className="object-cover"
                    unoptimized
                    priority
                  />
                ) : (
                  <div className="absolute inset-0 flex items-center justify-center text-muted-foreground">
                    <Film className="w-16 h-16 opacity-30" />
                  </div>
                )}
                {/* D&D overlay */}
                {isDraggingCover && (
                  <div className="absolute inset-0 bg-primary/30 backdrop-blur-sm flex flex-col items-center justify-center gap-2 z-10">
                    <Upload className="w-10 h-10 text-white drop-shadow" />
                    <span className="text-white text-sm font-semibold drop-shadow">ドロップしてカバーを設定</span>
                  </div>
                )}
                {uploadingCover && (
                  <div className="absolute inset-0 bg-black/50 flex items-center justify-center z-10">
                    <Loader2 className="w-10 h-10 animate-spin text-white" />
                  </div>
                )}
              </div>

              {uploadMsg && (
                <p className="text-xs text-center text-muted-foreground">{uploadMsg}</p>
              )}

              {/* Cover action buttons */}
              <div className="flex gap-1.5">
                <button
                  onClick={handleOpenCoverPicker}
                  className="flex-1 flex items-center justify-center gap-1.5 py-1.5 text-xs text-muted-foreground hover:text-foreground rounded-lg hover:bg-muted/50 border border-border/30 hover:border-border/60 transition-colors"
                >
                  <Layers className="w-3.5 h-3.5" /> 画像を選択
                </button>
                <label className="flex-1 flex items-center justify-center gap-1.5 py-1.5 text-xs text-muted-foreground hover:text-foreground rounded-lg hover:bg-muted/50 border border-border/30 hover:border-border/60 transition-colors cursor-pointer">
                  <Upload className="w-3.5 h-3.5" /> ファイルを選択
                  <input
                    type="file"
                    accept="image/*"
                    className="hidden"
                    onChange={async (e) => {
                      const file = e.target.files?.[0];
                      if (!file || !work) return;
                      setUploadingCover(true);
                      try {
                        await uploadCover(work.id, file);
                        await queryClient.invalidateQueries({ queryKey: ["work", work.id] });
                        setUploadMsg("カバー画像を登録しました");
                      } catch { setUploadMsg("アップロードに失敗しました"); }
                      finally { setUploadingCover(false); setTimeout(() => setUploadMsg(null), 3000); e.target.value = ""; }
                    }}
                  />
                </label>
              </div>

              {/* Rating & Memo — below cover */}
              <div className="bg-card/40 backdrop-blur rounded-xl p-4 border border-border/40 flex flex-col gap-3">
                <div>
                  <p className="text-xs text-muted-foreground uppercase tracking-wider mb-2">評価</p>
                  <div className="flex items-center gap-1">
                    {[1, 2, 3, 4, 5].map((star) => (
                      <button
                        key={star}
                        onClick={() => handleRating(star)}
                        title={`${star}点`}
                        className={`transition-colors ${(rating ?? 0) >= star ? "text-amber-400" : "text-muted-foreground/30 hover:text-amber-300"}`}
                      >
                        <Star className="w-6 h-6" fill={(rating ?? 0) >= star ? "currentColor" : "none"} />
                      </button>
                    ))}
                    {rating && (
                      <button onClick={() => handleRating(rating)} className="ml-2 text-xs text-muted-foreground hover:text-foreground">
                        クリア
                      </button>
                    )}
                  </div>
                </div>
                <div>
                  <p className="text-xs text-muted-foreground uppercase tracking-wider mb-2">メモ</p>
                  <textarea
                    value={memo}
                    onChange={(e) => setMemo(e.target.value)}
                    onBlur={handleMemoBlur}
                    placeholder="メモを入力..."
                    rows={3}
                    className="w-full bg-muted/40 border border-border/40 rounded-lg px-3 py-2 text-sm text-foreground placeholder:text-muted-foreground/50 focus:outline-none focus:ring-1 focus:ring-primary/50 resize-none"
                  />
                </div>
              </div>

            </div>

            {/* Right — Info */}
            <div className="flex-1 flex flex-col gap-6 min-w-0">
              {/* Title & Identifier */}
              <div>
                <div className="flex flex-wrap items-center gap-2 mb-2">
                  <span className="text-xs font-mono bg-primary/20 text-primary px-2 py-0.5 rounded border border-primary/30">
                    {work.primaryIdentifier}
                  </span>
                  {isComplete ? (
                    <span className="inline-flex items-center gap-1 text-[11px] text-emerald-400 bg-emerald-400/10 border border-emerald-400/20 px-2 py-0.5 rounded-full">
                      <CheckCircle2 className="w-3 h-3" /> 完了
                    </span>
                  ) : (
                    <span className="inline-flex items-center gap-1 text-[11px] text-amber-400 bg-amber-400/10 border border-amber-400/20 px-2 py-0.5 rounded-full">
                      <AlertCircle className="w-3 h-3" /> 未完了
                    </span>
                  )}
                  <button
                    onClick={handleFavorite}
                    title={favorite ? "お気に入り解除" : "お気に入りに追加"}
                    className={`p-1.5 rounded-full transition-colors ${favorite ? "text-rose-400 bg-rose-400/10" : "text-muted-foreground hover:text-rose-400 hover:bg-rose-400/10"}`}
                  >
                    <Heart className="w-4 h-4" fill={favorite ? "currentColor" : "none"} />
                  </button>
                  {releaseDate && (
                    <span className="text-xs text-muted-foreground flex items-center gap-1">
                      <Calendar className="w-3 h-3" /> {releaseDate}
                    </span>
                  )}
                  {runtime && (
                    <span className="text-xs text-muted-foreground flex items-center gap-1">
                      <Clock className="w-3 h-3" /> {runtime}分
                    </span>
                  )}
                </div>
                {title ? (
                  <h1 className="text-2xl md:text-3xl lg:text-4xl font-bold leading-tight tracking-tight">
                    {title}
                  </h1>
                ) : (
                  <div className="h-8 w-64 bg-muted/60 rounded animate-pulse" />
                )}
              </div>

              {/* Key Info Grid */}
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                {actress.length > 0 && (
                  <InfoBlock icon={<Tag className="w-4 h-4 text-pink-400" />} label="出演者">
                    <div className="flex flex-wrap gap-1.5">
                      {actress.map((a, i) => (
                        <button key={i} onClick={() => filterBy(a)} className="text-sm bg-pink-500/10 text-pink-300 border border-pink-500/20 px-2.5 py-0.5 rounded-full hover:bg-pink-500/25 transition-colors cursor-pointer">
                          {a}
                        </button>
                      ))}
                    </div>
                  </InfoBlock>
                )}

                {maker && (
                  <InfoBlock icon={<Building2 className="w-4 h-4 text-blue-400" />} label="メーカー">
                    <button onClick={() => filterBy(maker)} className="text-sm font-medium hover:text-blue-400 transition-colors text-left cursor-pointer">{maker}</button>
                  </InfoBlock>
                )}

                {label && (
                  <InfoBlock icon={<Layers className="w-4 h-4 text-violet-400" />} label="レーベル">
                    <button onClick={() => filterBy(label)} className="text-sm font-medium hover:text-violet-400 transition-colors text-left cursor-pointer">{label}</button>
                  </InfoBlock>
                )}

                {series && (
                  <InfoBlock icon={<Film className="w-4 h-4 text-amber-400" />} label="シリーズ">
                    <button onClick={() => filterBy(series)} className="text-sm font-medium hover:text-amber-400 transition-colors text-left cursor-pointer">{series}</button>
                  </InfoBlock>
                )}
              </div>

              {/* Genres / Tags */}
              {(() => {
                const userTags = work.metadata
                  .filter((m) => m.fieldName === "UserTag")
                  .map((m) => m.value);
                const hasContent = genres.length > 0 || userTags.length > 0 || tagEditing;
                if (!hasContent) return null;

                const handleAddTag = async () => {
                  const val = tagInput.trim();
                  if (!val) return;
                  try {
                    await addUserTag(resolvedParams.id, val);
                    setTagInput("");
                    await queryClient.invalidateQueries({ queryKey: ["work", resolvedParams.id] });
                  } catch { /* ignore */ }
                };

                const handleConfirmDelete = async () => {
                  if (!tagDeleteConfirm) return;
                  try {
                    if (tagDeleteConfirm.isScraped) {
                      await deleteGenreTag(resolvedParams.id, tagDeleteConfirm.tag);
                    } else {
                      await deleteUserTag(resolvedParams.id, tagDeleteConfirm.tag);
                    }
                    await queryClient.invalidateQueries({ queryKey: ["work", resolvedParams.id] });
                  } catch { /* ignore */ }
                  finally { setTagDeleteConfirm(null); }
                };

                return (
                  <>
                    {/* Tag Delete Confirm Modal */}
                    {tagDeleteConfirm && (
                      <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
                        <div className="bg-card border border-border rounded-2xl p-6 w-full max-w-sm mx-4 shadow-2xl">
                          <h2 className="text-base font-bold mb-1">タグを削除しますか？</h2>
                          <p className="text-sm text-muted-foreground mb-3">
                            タグ <span className="font-semibold text-foreground">「{tagDeleteConfirm.tag}」</span> をDBから削除します。
                          </p>
                          {tagDeleteConfirm.isScraped && (
                            <p className="text-xs text-destructive bg-destructive/10 border border-destructive/20 rounded-lg px-3 py-2 mb-4">
                              これはスクレイピング元のデータです。削除すると取り消しできません。
                            </p>
                          )}
                          <div className="flex gap-3">
                            <button
                              onClick={() => setTagDeleteConfirm(null)}
                              className="flex-1 py-2.5 rounded-xl border border-border text-sm hover:bg-muted/50 transition-colors"
                            >
                              キャンセル
                            </button>
                            <button
                              onClick={handleConfirmDelete}
                              className="flex-1 py-2.5 rounded-xl bg-destructive text-destructive-foreground text-sm font-bold hover:bg-destructive/90 transition-colors"
                            >
                              削除する
                            </button>
                          </div>
                        </div>
                      </div>
                    )}

                    <div>
                      <div className="flex items-center gap-2 mb-2">
                        <p className="text-xs text-muted-foreground uppercase tracking-wider">ジャンル / タグ</p>
                        <button
                          onClick={() => setTagEditing((v) => !v)}
                          className={`p-0.5 rounded transition-colors ${tagEditing ? "text-primary" : "text-muted-foreground hover:text-foreground"}`}
                          title="タグを編集"
                        >
                          <Pencil className="w-3 h-3" />
                        </button>
                      </div>
                      <div className="flex flex-wrap gap-1.5">
                        {/* Scraped genre tags */}
                        {genres.map((g, i) => (
                          <span key={`genre-${i}`} className="inline-flex items-center gap-1 text-xs bg-muted/80 border border-border/50 px-2.5 py-0.5 rounded-full text-muted-foreground">
                            {tagEditing ? (
                              <span>{g}</span>
                            ) : (
                              <button onClick={() => filterBy(g)} className="hover:text-foreground transition-colors">
                                {g}
                              </button>
                            )}
                            {tagEditing && (
                              <button
                                onClick={() => setTagDeleteConfirm({ tag: g, isScraped: true })}
                                className="ml-0.5 text-destructive/60 hover:text-destructive transition-colors leading-none"
                                title="タグを削除"
                              >
                                <X className="w-3 h-3" />
                              </button>
                            )}
                          </span>
                        ))}
                        {/* User tags */}
                        {userTags.map((t, i) => (
                          <span key={`user-${i}`} className="inline-flex items-center gap-1 text-xs bg-transparent border border-primary/50 text-primary px-2.5 py-0.5 rounded-full">
                            {tagEditing ? (
                              <span>{t}</span>
                            ) : (
                              <button onClick={() => filterBy(t)} className="hover:text-primary/70 transition-colors">
                                {t}
                              </button>
                            )}
                            {tagEditing && (
                              <button
                                onClick={() => setTagDeleteConfirm({ tag: t, isScraped: false })}
                                className="ml-0.5 text-primary/50 hover:text-destructive transition-colors leading-none"
                                title="タグを削除"
                              >
                                <X className="w-3 h-3" />
                              </button>
                            )}
                          </span>
                        ))}
                        {/* Add tag input */}
                        {tagEditing && (
                          <span className="inline-flex items-center gap-1">
                            <input
                              type="text"
                              value={tagInput}
                              onChange={(e) => setTagInput(e.target.value)}
                              onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); handleAddTag(); } }}
                              placeholder="タグを追加..."
                              className="text-xs bg-muted/40 border border-border/60 rounded-full px-2.5 py-0.5 text-foreground placeholder:text-muted-foreground/50 focus:outline-none focus:ring-1 focus:ring-primary/50 w-28"
                            />
                            <button
                              onClick={handleAddTag}
                              disabled={!tagInput.trim()}
                              className="p-0.5 text-primary disabled:text-muted-foreground/30 hover:text-primary/70 transition-colors"
                              title="追加"
                            >
                              <Plus className="w-3.5 h-3.5" />
                            </button>
                          </span>
                        )}
                      </div>
                    </div>
                  </>
                );
              })()}

              {/* Video Player */}
              {videoSrc && activeVideoAsset && (
                <div>
                  <div className="flex items-center gap-2 mb-2">
                    <p className="text-xs text-muted-foreground uppercase tracking-wider">動画</p>
                    {videoAssets.length > 1 && (
                      <div className="flex bg-muted/50 rounded-full p-0.5 gap-0.5">
                        {videoAssets.map((v, i) => (
                          <button
                            key={v.id}
                            onClick={() => setVideoTabIdx(i)}
                            className={`px-2.5 py-0.5 rounded-full text-xs font-mono font-medium transition-colors ${videoTabIdx === i ? "bg-background shadow text-foreground" : "text-muted-foreground hover:text-foreground"}`}
                          >
                            {getVideoLabel(v.originalFilename)}
                          </button>
                        ))}
                      </div>
                    )}
                  </div>
                  <VideoPlayer
                    key={activeVideoAsset.id}
                    src={videoSrc}
                    assetId={activeVideoAsset.id}
                    filename={activeVideoAsset.originalFilename}
                  />
                </div>
              )}

              {/* Sample Images — filmstrip + lightbox */}
              {work.sampleImages && work.sampleImages.length > 0 && (
                <div>
                  <p className="text-xs text-muted-foreground uppercase tracking-wider mb-2">
                    サンプル画像 ({work.sampleImages.length}枚)
                  </p>
                  {/* Filmstrip */}
                  <div className="flex gap-2 overflow-x-auto pb-1 scrollbar-thin">
                    {work.sampleImages.map((img, i) => (
                      <button
                        key={i}
                        onClick={() => setSampleIdx(i)}
                        className="relative shrink-0 h-28 rounded-lg overflow-hidden border-2 transition-all hover:border-primary/60 focus:outline-none"
                        style={{ width: `${28 * (16/9)}px`, borderColor: i === sampleIdx ? 'hsl(var(--primary))' : 'transparent' }}
                      >
                        <Image
                          src={`${API_ORIGIN}${img}`}
                          alt={`サンプル ${i + 1}`}
                          fill
                          className="object-cover"
                          unoptimized
                        />
                      </button>
                    ))}
                  </div>
                  {/* Lightbox for selected sample */}
                  {sampleIdx >= 0 && (
                    <div className="mt-2 relative rounded-xl overflow-hidden bg-muted/60 shadow-lg">
                      <div className="relative aspect-video">
                        <Image
                          src={`${API_ORIGIN}${work.sampleImages[sampleIdx]}`}
                          alt={`サンプル ${sampleIdx + 1}`}
                          fill
                          className="object-contain"
                          unoptimized
                        />
                      </div>
                      {work.sampleImages.length > 1 && (
                        <>
                          <button
                            onClick={() => setSampleIdx(i => (i - 1 + work.sampleImages.length) % work.sampleImages.length)}
                            className="absolute left-2 top-1/2 -translate-y-1/2 bg-black/50 hover:bg-black/70 text-white rounded-full w-8 h-8 flex items-center justify-center text-lg transition-colors"
                          >‹</button>
                          <button
                            onClick={() => setSampleIdx(i => (i + 1) % work.sampleImages.length)}
                            className="absolute right-2 top-1/2 -translate-y-1/2 bg-black/50 hover:bg-black/70 text-white rounded-full w-8 h-8 flex items-center justify-center text-lg transition-colors"
                          >›</button>
                          <span className="absolute bottom-2 right-3 text-[11px] text-white/70 bg-black/40 px-2 py-0.5 rounded-full">
                            {sampleIdx + 1} / {work.sampleImages.length}
                          </span>
                        </>
                      )}
                    </div>
                  )}
                </div>
              )}

              {/* Assets list */}
              {work.assets.length > 0 && (
                <div>
                  <p className="text-xs text-muted-foreground uppercase tracking-wider mb-2">ファイル</p>
                  <ul className="space-y-1.5">
                    {work.assets.map((asset) => (
                      <li key={asset.id} className="flex items-center justify-between text-xs bg-muted/40 border border-border/40 rounded-lg px-3 py-2">
                        <span className="font-mono truncate text-foreground/80" title={asset.originalFilename}>
                          {asset.originalFilename}
                        </span>
                        <span className="text-muted-foreground ml-3 shrink-0">
                          {(asset.fileSize / 1024 / 1024 / 1024).toFixed(2)} GB
                        </span>
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {/* History Timeline */}
              {work.history.length > 0 && (
                <div>
                  <div className="flex items-center gap-1.5 mb-3">
                    <History className="w-3.5 h-3.5 text-muted-foreground" />
                    <p className="text-xs text-muted-foreground uppercase tracking-wider">履歴</p>
                  </div>
                  <ol className="relative border-l border-border/40 space-y-3 pl-4">
                    {work.history.map((h, i) => (
                      <li key={i} className="relative">
                        <span className="absolute -left-[17px] top-1 w-2.5 h-2.5 rounded-full bg-primary/30 border border-primary/50" />
                        <p className="text-sm font-medium text-foreground/90 leading-snug">
                          {EVENT_LABELS[h.eventType] ?? h.eventType}
                        </p>
                        <time className="text-[11px] text-muted-foreground">
                          {new Date(h.occurredAt).toLocaleString("ja-JP")}
                        </time>
                      </li>
                    ))}
                  </ol>
                </div>
              )}

              {/* Developer Mode */}
              <div className="border-t border-border/30 pt-4">
                <button
                  onClick={() => setDevMode((v) => !v)}
                  className="flex items-center gap-2 text-xs text-muted-foreground hover:text-foreground transition-colors"
                >
                  {devMode ? <ChevronUp className="w-3 h-3" /> : <ChevronDown className="w-3 h-3" />}
                  Developer Mode
                </button>

                {devMode && (
                  <div className="mt-4 space-y-4">
                    {/* Diagnostic — provider info */}
                    <div className="bg-muted/30 rounded-xl p-4 border border-border/40">
                      <p className="text-xs font-mono text-muted-foreground mb-3 uppercase tracking-wider">
                        Provider Diagnostics
                      </p>
                      {work.diagnostic && (
                        <div className="flex items-center gap-4 mb-3">
                          <span className="text-muted-foreground text-xs">Confidence</span>
                          <span className="text-emerald-400 font-bold">{work.diagnostic.confidence ?? "--"}%</span>
                          <span className="text-muted-foreground text-xs">Decision</span>
                          <span className="text-primary text-xs font-semibold">{work.diagnostic.decision}</span>
                        </div>
                      )}
                      <div className="space-y-1">
                        {work.metadata.map((m, i) => (
                          <div key={i} className="text-xs flex items-start gap-2 font-mono">
                            <span className="text-violet-400 w-6 shrink-0">{m.isPrimary ? "★" : "·"}</span>
                            <span className="text-muted-foreground w-28 shrink-0">{m.fieldName}</span>
                            <span className="text-blue-400 w-20 shrink-0">{m.providerId}</span>
                            <span className="text-emerald-400 w-10 shrink-0">{m.confidenceScore}%</span>
                            <span className="text-foreground/70 break-all">{m.value}</span>
                          </div>
                        ))}
                      </div>
                    </div>

                    {/* Identifier Resolution */}
                    {work.diagnostic && (
                      <div className="bg-muted/30 rounded-xl p-4 border border-border/40">
                        <p className="text-xs font-mono text-muted-foreground mb-3 uppercase tracking-wider">
                          Identifier Resolution
                        </p>
                        <div className="space-y-1">
                          {work.diagnostic.evidences?.map((e, i) => (
                            <div key={i} className="text-xs flex items-center gap-2 font-mono">
                              <span className="text-muted-foreground w-24 shrink-0">{e.provider}</span>
                              <span className="flex-1 truncate text-foreground/70">{e.value}</span>
                              <span className="text-emerald-400">+{e.score}</span>
                            </div>
                          ))}
                        </div>
                      </div>
                    )}

                    {/* Event History */}
                    {work.history.length > 0 && (
                      <div className="bg-muted/30 rounded-xl p-4 border border-border/40">
                        <p className="text-xs font-mono text-muted-foreground mb-3 uppercase tracking-wider">
                          Event Log
                        </p>
                        <div className="space-y-2">
                          {work.history.map((h, i) => (
                            <div key={i} className="text-xs border-l-2 border-primary/30 pl-3">
                              <div className="flex items-center gap-2 text-muted-foreground mb-0.5">
                                <span className="font-mono">{new Date(h.occurredAt).toLocaleString("ja-JP")}</span>
                              </div>
                              <p className="font-medium text-foreground/90">
                                {EVENT_LABELS[h.eventType] ?? h.eventType}
                              </p>
                              {h.payload && (
                                <p className="text-muted-foreground mt-0.5 font-mono text-[10px] break-all">{h.payload}</p>
                              )}
                            </div>
                          ))}
                        </div>
                      </div>
                    )}
                  </div>
                )}
              </div>
            </div>
          </div>
        </div>
      </div>
    </main>
  );
}

function InfoBlock({
  icon,
  label,
  children,
}: {
  icon: React.ReactNode;
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="bg-card/40 backdrop-blur rounded-xl p-4 border border-border/40">
      <div className="flex items-center gap-1.5 mb-2">
        {icon}
        <span className="text-xs text-muted-foreground uppercase tracking-wider">{label}</span>
      </div>
      {children}
    </div>
  );
}

const SPEEDS = [0.5, 0.75, 1, 1.25, 1.5, 2] as const;
const RESUME_THRESHOLD_SEC = 5;

function formatTime(sec: number): string {
  if (!isFinite(sec)) return "--:--";
  const h = Math.floor(sec / 3600);
  const m = Math.floor((sec % 3600) / 60);
  const s = Math.floor(sec % 60);
  if (h > 0) return `${h}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
  return `${m}:${String(s).padStart(2, "0")}`;
}

function VideoPlayer({ src, assetId, filename }: { src: string; assetId: string; filename: string }) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [speed, setSpeed] = useState(1);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration] = useState(0);
  const storageKey = `wise-video-pos-${assetId}`;

  const handleLoadedMetadata = () => {
    const el = videoRef.current;
    if (!el) return;
    setDuration(el.duration);
    const saved = parseFloat(localStorage.getItem(storageKey) ?? "0");
    if (saved > RESUME_THRESHOLD_SEC && saved < el.duration - RESUME_THRESHOLD_SEC) {
      el.currentTime = saved;
    }
  };

  const handleTimeUpdate = () => {
    const el = videoRef.current;
    if (!el) return;
    setCurrentTime(el.currentTime);
    localStorage.setItem(storageKey, String(Math.floor(el.currentTime)));
  };

  const handleSpeedChange = (s: number) => {
    setSpeed(s);
    if (videoRef.current) videoRef.current.playbackRate = s;
  };

  return (
    <div className="space-y-2">
      <video
        ref={videoRef}
        controls
        className="w-full rounded-xl shadow-xl bg-black aspect-video"
        src={src}
        preload="metadata"
        onLoadedMetadata={handleLoadedMetadata}
        onTimeUpdate={handleTimeUpdate}
      />
      {/* Controls bar */}
      <div className="flex items-center justify-between gap-3 px-1">
        {/* Time */}
        <span className="text-xs font-mono text-muted-foreground tabular-nums shrink-0">
          {formatTime(currentTime)} / {formatTime(duration)}
        </span>

        {/* Speed */}
        <div className="flex items-center gap-0.5 bg-muted/40 rounded-full p-0.5">
          {SPEEDS.map((s) => (
            <button
              key={s}
              onClick={() => handleSpeedChange(s)}
              className={`px-2 py-0.5 rounded-full text-[11px] font-mono transition-colors ${
                speed === s
                  ? "bg-background shadow text-foreground"
                  : "text-muted-foreground hover:text-foreground"
              }`}
            >
              {s}x
            </button>
          ))}
        </div>

        {/* Filename */}
        <span className="text-[11px] text-muted-foreground/60 truncate max-w-[180px]" title={filename}>
          {filename}
        </span>
      </div>
    </div>
  );
}
