"use client";

import { useGalleryStore } from "@/store/useGalleryStore";
import { ArrowLeft, Monitor, Moon, Sun, Settings2, Globe, Image, Trash2, Cookie, CheckCircle2, AlertCircle } from "lucide-react";
import Link from "next/link";
import { useEffect, useState } from "react";
import { fetchSettings, patchSetting, clearHistory, clearFinishedJobs } from "@/lib/api";
import { useT } from "@/hooks/useT";
import { API_BASE_URL } from "@/lib/api";

interface Fc2CookieStatus {
  hasCookieTxt: boolean;
  hasStorageState: boolean;
  cookiePreview: string | null;
  storageStatePath: string;
  cookieTxtPath: string;
}

export default function SettingsPage() {
  const { theme, setTheme, language, setLanguage } = useGalleryStore();
  const t = useT();
  const [mounted, setMounted] = useState(false);
  const [downloadSampleImages, setDownloadSampleImages] = useState(false);
  const [saving, setSaving] = useState(false);
  const [maintMsg, setMaintMsg] = useState<string | null>(null);

  // FC2 Cookie state
  const [fc2CookieInput, setFc2CookieInput] = useState("");
  const [fc2CookieStatus, setFc2CookieStatus] = useState<Fc2CookieStatus | null>(null);
  const [fc2Saving, setFc2Saving] = useState(false);
  const [fc2Msg, setFc2Msg] = useState<{ text: string; ok: boolean } | null>(null);

  const loadFc2Status = async () => {
    try {
      const res = await fetch(`${API_BASE_URL}/system/cookies/fc2/status`);
      if (res.ok) setFc2CookieStatus(await res.json());
    } catch { /* ignore */ }
  };

  // MGS Cookie state
  interface MgsCookieStatus { hasCookieTxt: boolean; cookiePreview: string | null; cookieTxtPath: string; }
  const [mgsCookieInput, setMgsCookieInput] = useState("");
  const [mgsCookieStatus, setMgsCookieStatus] = useState<MgsCookieStatus | null>(null);
  const [mgsSaving, setMgsSaving] = useState(false);
  const [mgsMsg, setMgsMsg] = useState<{ text: string; ok: boolean } | null>(null);

  const loadMgsStatus = async () => {
    try {
      const res = await fetch(`${API_BASE_URL}/system/cookies/mgs/status`);
      if (res.ok) setMgsCookieStatus(await res.json());
    } catch { /* ignore */ }
  };

  const handleSaveMgsCookie = async () => {
    setMgsSaving(true);
    setMgsMsg(null);
    try {
      const res = await fetch(`${API_BASE_URL}/system/cookies/mgs`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ cookies: mgsCookieInput }),
      });
      if (!res.ok) throw new Error("Failed");
      setMgsMsg({ text: "保存しました。次回スキャン時から有効になります。", ok: true });
      setMgsCookieInput("");
      await loadMgsStatus();
    } catch {
      setMgsMsg({ text: "保存に失敗しました。", ok: false });
    } finally {
      setMgsSaving(false);
      setTimeout(() => setMgsMsg(null), 4000);
    }
  };

  const handleClearMgsCookie = async () => {
    setMgsSaving(true);
    try {
      await fetch(`${API_BASE_URL}/system/cookies/mgs`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ cookies: "" }),
      });
      setMgsMsg({ text: "Cookie を削除しました。", ok: true });
      await loadMgsStatus();
    } catch {
      setMgsMsg({ text: "削除に失敗しました。", ok: false });
    } finally {
      setMgsSaving(false);
      setTimeout(() => setMgsMsg(null), 4000);
    }
  };

  useEffect(() => {
    setMounted(true);
    fetchSettings().then((settings) => {
      const s = settings.find(s => s.key === 'downloadSampleImages');
      if (s) setDownloadSampleImages(s.value === 'true');
    }).catch(() => {});
    loadFc2Status();
    loadMgsStatus();
  }, []);

  const handleSaveFc2Cookie = async () => {
    setFc2Saving(true);
    setFc2Msg(null);
    try {
      const res = await fetch(`${API_BASE_URL}/system/cookies/fc2`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ cookies: fc2CookieInput }),
      });
      if (!res.ok) throw new Error("Failed");
      setFc2Msg({ text: "保存しました。次回スキャン時から有効になります。", ok: true });
      setFc2CookieInput("");
      await loadFc2Status();
    } catch {
      setFc2Msg({ text: "保存に失敗しました。", ok: false });
    } finally {
      setFc2Saving(false);
      setTimeout(() => setFc2Msg(null), 4000);
    }
  };

  const handleClearFc2Cookie = async () => {
    setFc2Saving(true);
    try {
      await fetch(`${API_BASE_URL}/system/cookies/fc2`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ cookies: "" }),
      });
      setFc2Msg({ text: "Cookie を削除しました。", ok: true });
      await loadFc2Status();
    } catch {
      setFc2Msg({ text: "削除に失敗しました。", ok: false });
    } finally {
      setFc2Saving(false);
      setTimeout(() => setFc2Msg(null), 4000);
    }
  };

  const handleToggleSampleImages = async () => {
    const next = !downloadSampleImages;
    setDownloadSampleImages(next);
    setSaving(true);
    try {
      await patchSetting('downloadSampleImages', String(next));
    } catch {
      setDownloadSampleImages(!next);
    } finally {
      setSaving(false);
    }
  };

  if (!mounted) return null;

  return (
    <main className="min-h-screen bg-background text-foreground">
      <header className="sticky top-0 z-50 w-full border-b bg-background/95 backdrop-blur">
        <div className="container flex h-16 items-center px-4 md:px-6">
          <Link href="/" className="flex items-center gap-2 text-muted-foreground hover:text-foreground transition-colors mr-6">
            <ArrowLeft className="w-4 h-4" /> Back
          </Link>
          <div className="flex items-center gap-2 font-bold text-xl">
            <Settings2 className="w-5 h-5 text-primary" />
            <span>{t("settings.title")}</span>
          </div>
        </div>
      </header>

      <div className="container max-w-3xl mx-auto py-10 px-4 md:px-6">
        <div className="space-y-8">
          
          {/* Theme Settings */}
          <section className="space-y-4">
            <h2 className="text-xl font-semibold border-b pb-2">{t("settings.appearance")}</h2>
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              <button onClick={() => setTheme('light')} className={`flex flex-col items-center justify-center p-6 rounded-xl border-2 transition-all ${theme === 'light' ? 'border-primary bg-primary/10' : 'border-border hover:border-primary/50'}`}>
                <Sun className="w-8 h-8 mb-3" />
                <span className="font-medium">{t("settings.light")}</span>
              </button>
              <button onClick={() => setTheme('dark')} className={`flex flex-col items-center justify-center p-6 rounded-xl border-2 transition-all ${theme === 'dark' ? 'border-primary bg-primary/10' : 'border-border hover:border-primary/50'}`}>
                <Moon className="w-8 h-8 mb-3" />
                <span className="font-medium">{t("settings.dark")}</span>
              </button>
              <button onClick={() => setTheme('system')} className={`flex flex-col items-center justify-center p-6 rounded-xl border-2 transition-all ${theme === 'system' ? 'border-primary bg-primary/10' : 'border-border hover:border-primary/50'}`}>
                <Monitor className="w-8 h-8 mb-3" />
                <span className="font-medium">{t("settings.system")}</span>
              </button>
            </div>
          </section>

          {/* Scraping Settings */}
          <section className="space-y-4 pt-4">
            <h2 className="text-xl font-semibold border-b pb-2 flex items-center gap-2">
              <Image className="w-5 h-5" /> {t("settings.scraping")}
            </h2>
            <div className="bg-card border rounded-xl overflow-hidden">
              <div className="flex items-center justify-between p-4">
                <div>
                  <p className="font-medium">{t("settings.downloadSamples")}</p>
                  <p className="text-sm text-muted-foreground mt-0.5">{t("settings.downloadSamplesDesc")}</p>
                </div>
                <button
                  onClick={handleToggleSampleImages}
                  disabled={saving}
                  className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none ${downloadSampleImages ? 'bg-primary' : 'bg-muted'} ${saving ? 'opacity-50' : ''}`}
                >
                  <span className={`inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform ${downloadSampleImages ? 'translate-x-6' : 'translate-x-1'}`} />
                </button>
              </div>
            </div>
          </section>

          {/* Maintenance */}
          <section className="space-y-4 pt-4">
            <h2 className="text-xl font-semibold border-b pb-2 flex items-center gap-2">
              <Trash2 className="w-5 h-5" /> {t("settings.maintenance")}
            </h2>
            {maintMsg && <p className="text-sm text-muted-foreground">{maintMsg}</p>}
            <div className="bg-card border rounded-xl overflow-hidden divide-y divide-border">
              <div className="flex items-center justify-between p-4">
                <div>
                  <p className="font-medium">{t("settings.clearHistory")}</p>
                  <p className="text-sm text-muted-foreground mt-0.5">{t("settings.clearHistoryDesc")}</p>
                </div>
                <button onClick={async () => { try { const r = await clearHistory(); setMaintMsg(`${r.deleted}件の履歴を削除しました`); } catch { setMaintMsg("削除に失敗しました"); } setTimeout(() => setMaintMsg(null), 3000); }} className="px-3 py-1.5 rounded-lg bg-destructive/10 text-destructive hover:bg-destructive/20 text-sm font-medium transition-colors">
                  {t("settings.delete")}
                </button>
              </div>
              <div className="flex items-center justify-between p-4">
                <div>
                  <p className="font-medium">{t("settings.clearJobs")}</p>
                  <p className="text-sm text-muted-foreground mt-0.5">{t("settings.clearJobsDesc")}</p>
                </div>
                <button onClick={async () => { try { const r = await clearFinishedJobs(); setMaintMsg(`${r.deleted}件のジョブを削除しました`); } catch { setMaintMsg("削除に失敗しました"); } setTimeout(() => setMaintMsg(null), 3000); }} className="px-3 py-1.5 rounded-lg bg-destructive/10 text-destructive hover:bg-destructive/20 text-sm font-medium transition-colors">
                  {t("settings.delete")}
                </button>
              </div>
            </div>
          </section>

          {/* FC2 Cookie Settings */}
          <section className="space-y-4 pt-4">
            <h2 className="text-xl font-semibold border-b pb-2 flex items-center gap-2">
              <Cookie className="w-5 h-5" /> FC2 年齢確認 Cookie
            </h2>
            <p className="text-sm text-muted-foreground">
              FC2コンテンツマーケットの年齢確認ゲートを突破するため、ブラウザのセッションCookieを貼り付けます。<br />
              ブラウザで <code className="bg-muted px-1 rounded text-xs">adult.contents.fc2.com</code> にアクセスして年齢確認後、DevTools → Application → Cookies からコピーしてください。
            </p>

            {/* 現在の状態 */}
            <div className="bg-card border rounded-xl p-4 space-y-2">
              <div className="flex items-center gap-2 text-sm">
                {fc2CookieStatus?.hasCookieTxt || fc2CookieStatus?.hasStorageState ? (
                  <CheckCircle2 className="w-4 h-4 text-green-500 shrink-0" />
                ) : (
                  <AlertCircle className="w-4 h-4 text-amber-500 shrink-0" />
                )}
                <span className="font-medium">
                  {fc2CookieStatus?.hasCookieTxt
                    ? "fc2Cookies.txt が設定済み"
                    : fc2CookieStatus?.hasStorageState
                    ? "fc2StorageState.json が設定済み"
                    : "Cookie 未設定（FC2のMaker取得が制限される場合があります）"}
                </span>
              </div>
              {fc2CookieStatus?.cookiePreview && (
                <p className="text-xs text-muted-foreground font-mono bg-muted rounded px-2 py-1 truncate">
                  {fc2CookieStatus.cookiePreview}
                </p>
              )}
              {fc2CookieStatus?.hasCookieTxt && (
                <button
                  onClick={handleClearFc2Cookie}
                  disabled={fc2Saving}
                  className="text-xs text-destructive hover:underline"
                >
                  Cookie を削除する
                </button>
              )}
            </div>

            {/* Cookie 入力 */}
            <div className="space-y-2">
              <label className="text-sm font-medium">
                Cookie 文字列を貼り付け
                <span className="ml-2 text-xs text-muted-foreground font-normal">（例: name1=value1; name2=value2）</span>
              </label>
              <textarea
                className="w-full rounded-lg border bg-background px-3 py-2 text-sm font-mono resize-none focus:outline-none focus:ring-2 focus:ring-primary"
                rows={3}
                placeholder="_fc2id=xxx; age_check=1; ..."
                value={fc2CookieInput}
                onChange={(e) => setFc2CookieInput(e.target.value)}
              />
              <div className="flex items-center gap-3">
                <button
                  onClick={handleSaveFc2Cookie}
                  disabled={fc2Saving || !fc2CookieInput.trim()}
                  className="px-4 py-2 rounded-lg bg-primary text-primary-foreground text-sm font-medium hover:bg-primary/90 disabled:opacity-50 transition-colors"
                >
                  {fc2Saving ? "保存中…" : "保存する"}
                </button>
                {fc2Msg && (
                  <span className={`text-sm ${fc2Msg.ok ? "text-green-600" : "text-destructive"}`}>
                    {fc2Msg.text}
                  </span>
                )}
              </div>
              <p className="text-xs text-muted-foreground">
                または <code className="bg-muted px-1 rounded">{fc2CookieStatus?.storageStatePath ?? "%APPDATA%\\WISE\\fc2StorageState.json"}</code> にPlaywright storageState形式で配置することもできます。
              </p>
            </div>
          </section>

          {/* MGS Cookie Settings */}
          <section className="space-y-4 pt-4">
            <h2 className="text-xl font-semibold border-b pb-2 flex items-center gap-2">
              <Cookie className="w-5 h-5" /> MGStage 年齢確認 Cookie
            </h2>
            <p className="text-sm text-muted-foreground">
              MGStageの年齢確認ゲートを突破するためのセッションCookieを設定します。<br />
              ブラウザで <code className="bg-muted px-1 rounded text-xs">mgstage.com</code> にログイン後、DevTools → Application → Cookies から <code className="bg-muted px-1 rounded text-xs">sess</code> など必要なCookieをコピーしてください。
            </p>
            <div className="bg-card border rounded-xl p-4 space-y-2">
              <div className="flex items-center gap-2 text-sm">
                {mgsCookieStatus?.hasCookieTxt ? (
                  <CheckCircle2 className="w-4 h-4 text-green-500 shrink-0" />
                ) : (
                  <AlertCircle className="w-4 h-4 text-amber-500 shrink-0" />
                )}
                <span className="font-medium">
                  {mgsCookieStatus?.hasCookieTxt
                    ? "mgsCookies.txt が設定済み（adc=1 と合わせて送信）"
                    : "Cookie 未設定（adc=1 のみ — タイトル取得が制限される場合があります）"}
                </span>
              </div>
              {mgsCookieStatus?.cookiePreview && (
                <p className="text-xs text-muted-foreground font-mono bg-muted rounded px-2 py-1 truncate">
                  {mgsCookieStatus.cookiePreview}
                </p>
              )}
              {mgsCookieStatus?.hasCookieTxt && (
                <button
                  onClick={handleClearMgsCookie}
                  disabled={mgsSaving}
                  className="text-xs text-destructive hover:underline"
                >
                  Cookie を削除する
                </button>
              )}
            </div>
            <div className="space-y-2">
              <label className="text-sm font-medium">
                Cookie 文字列を貼り付け
                <span className="ml-2 text-xs text-muted-foreground font-normal">（例: sess=abc123; age_check=1）</span>
              </label>
              <textarea
                className="w-full rounded-lg border bg-background px-3 py-2 text-sm font-mono resize-none focus:outline-none focus:ring-2 focus:ring-primary"
                rows={3}
                placeholder="sess=xxx; age_check=1; ..."
                value={mgsCookieInput}
                onChange={(e) => setMgsCookieInput(e.target.value)}
              />
              <div className="flex items-center gap-3">
                <button
                  onClick={handleSaveMgsCookie}
                  disabled={mgsSaving || !mgsCookieInput.trim()}
                  className="px-4 py-2 rounded-lg bg-primary text-primary-foreground text-sm font-medium hover:bg-primary/90 disabled:opacity-50 transition-colors"
                >
                  {mgsSaving ? "保存中…" : "保存する"}
                </button>
                {mgsMsg && (
                  <span className={`text-sm ${mgsMsg.ok ? "text-green-600" : "text-destructive"}`}>
                    {mgsMsg.text}
                  </span>
                )}
              </div>
              <p className="text-xs text-muted-foreground">
                保存先: <code className="bg-muted px-1 rounded">{mgsCookieStatus?.cookieTxtPath ?? "%APPDATA%\\WISE\\mgsCookies.txt"}</code>
              </p>
            </div>
          </section>

          {/* Language Settings */}
          <section className="space-y-4 pt-4">
            <h2 className="text-xl font-semibold border-b pb-2 flex items-center gap-2">
              <Globe className="w-5 h-5" /> {t("settings.language")}
            </h2>
            <div className="bg-card border rounded-xl overflow-hidden">
              <button onClick={() => setLanguage('ja')} className={`w-full flex items-center justify-between p-4 border-b transition-colors hover:bg-muted/50 ${language === 'ja' ? 'bg-primary/5' : ''}`}>
                <span className="font-medium">日本語</span>
                {language === 'ja' && <span className="w-3 h-3 rounded-full bg-primary"></span>}
              </button>
              <button onClick={() => setLanguage('en')} className={`w-full flex items-center justify-between p-4 transition-colors hover:bg-muted/50 ${language === 'en' ? 'bg-primary/5' : ''}`}>
                <span className="font-medium">English</span>
                {language === 'en' && <span className="w-3 h-3 rounded-full bg-primary"></span>}
              </button>
            </div>
          </section>

        </div>
      </div>
    </main>
  );
}
