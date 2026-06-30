import { useGalleryStore } from "@/store/useGalleryStore";

const translations = {
  ja: {
    // Nav
    "nav.back": "ギャラリーに戻る",
    "nav.settings": "設定",
    "nav.import": "インポート",
    "nav.triage": "トリアージ",
    // Gallery
    "gallery.search": "タイトル・識別子で検索...",
    "gallery.noResults": "作品が見つかりません",
    "gallery.loading": "読み込み中...",
    // Work detail
    "detail.rescan": "再スキャン",
    "detail.openFolder": "ファイルの場所",
    "detail.delete": "削除",
    "detail.favorite": "お気に入り",
    "detail.rating": "評価",
    "detail.memo": "メモ",
    "detail.replaceCover": "サムネイルからカバーを差し替え",
    "detail.packageImages": "パッケージ画像（元に戻す）",
    "detail.thumbnails": "サムネイル / サンプル画像",
    "detail.noThumbnails": "画像が見つかりません",
    "detail.actress": "出演者",
    "detail.maker": "メーカー",
    "detail.label": "レーベル",
    "detail.series": "シリーズ",
    "detail.genre": "ジャンル",
    "detail.releaseDate": "発売日",
    "detail.runtime": "収録時間",
    "detail.deleteConfirm": "作品を削除しますか？",
    "detail.deleteFiles": "動画・カバー画像などの物理ファイルも削除する",
    "detail.cancel": "キャンセル",
    "detail.rescanQueued": "再スキャンをキューに追加しました",
    // Settings
    "settings.title": "設定",
    "settings.appearance": "外観",
    "settings.light": "ライト",
    "settings.dark": "ダーク",
    "settings.system": "システム",
    "settings.language": "言語",
    "settings.scraping": "スクレイピング",
    "settings.downloadSamples": "サンプル画像をダウンロード",
    "settings.downloadSamplesDesc": "メタデータ取得時にサンプル画像を .thumbnails/ に保存します",
    "settings.maintenance": "メンテナンス",
    "settings.clearHistory": "システム履歴を全削除",
    "settings.clearHistoryDesc": "EventLog テーブルを全件削除します",
    "settings.clearJobs": "完了済みジョブを全削除",
    "settings.clearJobsDesc": "Completed / Failed / Cancelled のジョブを削除します",
    "settings.delete": "削除",
    // Import
    "import.title": "インポート",
    "import.watchFolders": "監視フォルダー",
    "import.manual": "手動インポート",
    // Triage
    "triage.title": "トリアージ",
    "triage.rescan": "件を再スキャン",
    "triage.save": "保存",
    "triage.empty": "トリアージ対象の作品はありません",
    // List headers
    "list.id": "ID",
    "list.title": "タイトル",
    "list.actress": "出演者",
    "list.maker": "メーカー",
    "list.label": "レーベル",
    "list.releaseDate": "発売日",
  },
  en: {
    // Nav
    "nav.back": "Back to Gallery",
    "nav.settings": "Settings",
    "nav.import": "Import",
    "nav.triage": "Triage",
    // Gallery
    "gallery.search": "Search by title or identifier...",
    "gallery.noResults": "No works found",
    "gallery.loading": "Loading...",
    // Work detail
    "detail.rescan": "Re-scan",
    "detail.openFolder": "Open Location",
    "detail.delete": "Delete",
    "detail.favorite": "Favorite",
    "detail.rating": "Rating",
    "detail.memo": "Notes",
    "detail.replaceCover": "Replace cover from thumbnails",
    "detail.packageImages": "Package Images (revert)",
    "detail.thumbnails": "Thumbnails / Sample Images",
    "detail.noThumbnails": "No images found",
    "detail.actress": "Actress",
    "detail.maker": "Studio",
    "detail.label": "Label",
    "detail.series": "Series",
    "detail.genre": "Genre",
    "detail.releaseDate": "Release Date",
    "detail.runtime": "Runtime",
    "detail.deleteConfirm": "Delete this work?",
    "detail.deleteFiles": "Also delete video and cover files from disk",
    "detail.cancel": "Cancel",
    "detail.rescanQueued": "Re-scan queued",
    // Settings
    "settings.title": "Settings",
    "settings.appearance": "Appearance",
    "settings.light": "Light",
    "settings.dark": "Dark",
    "settings.system": "System",
    "settings.language": "Language",
    "settings.scraping": "Scraping",
    "settings.downloadSamples": "Download Sample Images",
    "settings.downloadSamplesDesc": "Save sample images to .thumbnails/ during metadata fetch",
    "settings.maintenance": "Maintenance",
    "settings.clearHistory": "Clear All System History",
    "settings.clearHistoryDesc": "Delete all entries from the EventLog table",
    "settings.clearJobs": "Clear Finished Jobs",
    "settings.clearJobsDesc": "Delete Completed / Failed / Cancelled jobs",
    "settings.delete": "Delete",
    // Import
    "import.title": "Import",
    "import.watchFolders": "Watch Folders",
    "import.manual": "Manual Import",
    // Triage
    "triage.title": "Triage",
    "triage.rescan": "re-scan selected",
    "triage.save": "Save",
    "triage.empty": "No works need triage",
    // List headers
    "list.id": "ID",
    "list.title": "Title",
    "list.actress": "Actress",
    "list.maker": "Studio",
    "list.label": "Label",
    "list.releaseDate": "Release",
  },
} as const;

type TranslationKey = keyof typeof translations.ja;

export function useT() {
  const language = useGalleryStore((s) => s.language);
  const dict = translations[language] as Record<string, string>;
  return (key: TranslationKey): string => dict[key] ?? (translations.ja as Record<string, string>)[key] ?? key;
}
