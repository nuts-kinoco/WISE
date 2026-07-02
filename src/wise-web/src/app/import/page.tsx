"use client";

import { useState, useEffect, useRef } from "react";
import { ArrowLeft, FolderOpen, ShieldAlert, Loader2, HardDriveUpload, Plus, Trash2, Power, PowerOff, CheckCircle2, Play, AlertCircle, FilePlus, Copy, FolderInput, XCircle, RefreshCw, ChevronsUpDown, ChevronUp, ChevronDown, ScanLine } from "lucide-react";
import Link from "next/link";
import { API_BASE_URL, openSystemPath } from "@/lib/api";

interface WatchFolder {
  id: string;
  path: string;
  isEnabled: boolean;
  createdAt: string;
}

interface AnalyzeResult {
  scannedDirectory: string;
  totalFiles: number;
  candidates: {
    filePath: string;
    fileName: string;
    fileSize: number;
    extractedIdentifier: string | null;
    isExisting: boolean;
  }[];
}

interface JobProgress {
  id: string;
  jobType: string;
  status: string; // "Created", "Queued", "Running", "Completed", "Failed", "Canceled"
  totalCount: number;
  processedCount: number;
  errorMessage: string | null;
  resultPayload: string | null;
}

export default function ImportPage() {
  const [watchFolders, setWatchFolders] = useState<WatchFolder[]>([]);
  const [newWatchFolderPath, setNewWatchFolderPath] = useState("");

  const [inputDirectory, setInputDirectory] = useState("");
  const [outputDirectory, setOutputDirectory] = useState("D:\\WISE_Library");
  const [importMode, setImportMode] = useState<"Move" | "Copy">("Copy");
  
  // Steps: 1 = Input, 2 = Preview, 3 = Progress, 4 = Completed
  const [step, setStep] = useState<1 | 2 | 3 | 4>(1);
  
  const [isAnalyzing, setIsAnalyzing] = useState(false);
  const [analyzeResult, setAnalyzeResult] = useState<AnalyzeResult | null>(null);
  
  const [isExecuting, setIsExecuting] = useState(false);
  const [browsingInput, setBrowsingInput] = useState(false);
  const [browsingOutput, setBrowsingOutput] = useState(false);
  const [excludedPaths, setExcludedPaths] = useState<Set<string>>(new Set());
  const [sortKey, setSortKey] = useState<"fileName" | "identifier" | "status" | null>(null);
  const [sortAsc, setSortAsc] = useState(true);
  
  const [currentJobId, setCurrentJobId] = useState<string | null>(null);
  const [jobProgress, setJobProgress] = useState<JobProgress | null>(null);
  const [isCanceling, setIsCanceling] = useState(false);
  const importStartTimeRef = useRef<number | null>(null);
  const [etaText, setEtaText] = useState<string | null>(null);
  const [activeJobs, setActiveJobs] = useState<{ id: string; jobType: string; status: string; totalCount: number; processedCount: number }[]>([]);

  const [error, setError] = useState("");

  useEffect(() => {
    setInputDirectory(localStorage.getItem("import_inputDir") ?? "");
    setOutputDirectory(localStorage.getItem("import_outputDir") ?? "D:\\WISE_Library");
    setImportMode((localStorage.getItem("import_mode") as "Move" | "Copy") ?? "Copy");
    fetchWatchFolders();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => { localStorage.setItem("import_inputDir", inputDirectory); }, [inputDirectory]);
  useEffect(() => { localStorage.setItem("import_outputDir", outputDirectory); }, [outputDirectory]);
  useEffect(() => { localStorage.setItem("import_mode", importMode); }, [importMode]);

  useEffect(() => {
    let intervalId: NodeJS.Timeout;
    if (step === 3 && currentJobId) {
      intervalId = setInterval(async () => {
        try {
          const res = await fetch(`${API_BASE_URL}/jobs/${currentJobId}`);
          if (res.ok) {
            const data = await res.json();
            setJobProgress(data);

            // ETA calculation
            if (importStartTimeRef.current !== null && data.totalCount > 0) {
              const ratio = data.processedCount / data.totalCount;
              if (ratio > 0.05) {
                const elapsedMs = Date.now() - importStartTimeRef.current;
                const totalEstimatedMs = elapsedMs / ratio;
                const remainingMs = totalEstimatedMs - elapsedMs;
                if (remainingMs > 0) {
                  const secs = Math.round(remainingMs / 1000);
                  if (secs < 60) {
                    setEtaText(`約 ${secs} 秒`);
                  } else {
                    const m = Math.floor(secs / 60);
                    const s = secs % 60;
                    setEtaText(s > 0 ? `約 ${m} 分 ${s} 秒` : `約 ${m} 分`);
                  }
                } else {
                  setEtaText(null);
                }
              } else {
                setEtaText(null);
              }
            }

            if (data.status === "Completed") {
              setStep(4);
              importStartTimeRef.current = null;
              setEtaText(null);
            } else if (data.status === "Failed" || data.status === "Canceled") {
              importStartTimeRef.current = null;
              setEtaText(null);
            }
          }
        } catch (err) {
          console.error("Failed to fetch job status", err);
        }

        // Also poll active jobs for scan status chip (step 3 only after import job moves on)
        try {
          const res2 = await fetch(`${API_BASE_URL}/jobs/active`);
          if (res2.ok) {
            const jobs = await res2.json();
            setActiveJobs(jobs.filter((j: any) => j.jobType === "FetchMetadata" || j.jobType === "fetchmetadata"));
          }
        } catch {
          // ignore
        }
      }, 1000);
    }

    return () => {
      if (intervalId) clearInterval(intervalId);
    };
  }, [step, currentJobId]);

  const fetchWatchFolders = async () => {
    try {
      const res = await fetch(`${API_BASE_URL}/watchfolders`);
      if (res.ok) {
        setWatchFolders(await res.json());
      }
    } catch (err) {
      console.error("Failed to fetch watch folders", err);
    }
  };

  const handleAddWatchFolder = async () => {
    if (!newWatchFolderPath) return;
    try {
      const res = await fetch(`${API_BASE_URL}/watchfolders`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ path: newWatchFolderPath })
      });
      if (res.ok) {
        setNewWatchFolderPath("");
        fetchWatchFolders();
      }
    } catch (err) {
      console.error(err);
    }
  };

  const handleDeleteWatchFolder = async (id: string) => {
    try {
      await fetch(`${API_BASE_URL}/watchfolders/${id}`, { method: "DELETE" });
      fetchWatchFolders();
    } catch (err) {
      console.error(err);
    }
  };

  const handleToggleWatchFolder = async (id: string) => {
    try {
      await fetch(`${API_BASE_URL}/watchfolders/${id}/toggle`, { method: "PATCH" });
      fetchWatchFolders();
    } catch (err) {
      console.error(err);
    }
  };

  const browseFolder = async (
    setter: (path: string) => void,
    setBrowsing: (v: boolean) => void,
    currentPath?: string
  ) => {
    setBrowsing(true);
    try {
      const params = currentPath ? `?initialPath=${encodeURIComponent(currentPath)}` : "";
      const res = await fetch(`${API_BASE_URL}/system/browse-folder${params}`);
      if (res.ok) {
        const data = await res.json();
        if (!data.cancelled && data.path) setter(data.path);
      }
    } catch (err) {
      console.error("Browse folder failed", err);
    } finally {
      setBrowsing(false);
    }
  };

  const handleAnalyze = async () => {
    if (!inputDirectory) return;
    setIsAnalyzing(true);
    setError("");

    try {
      const res = await fetch(`${API_BASE_URL}/import/analyze`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ directoryPath: inputDirectory }),
      });

      if (!res.ok) {
        throw new Error(await res.text());
      }
      
      const data = await res.json();
      setAnalyzeResult(data);
      setStep(2);
    } catch (err: any) {
      setError(err.message || "Failed to analyze directory");
    } finally {
      setIsAnalyzing(false);
    }
  };

  const handleExecute = async () => {
    setIsExecuting(true);
    setError("");

    try {
      const res = await fetch(`${API_BASE_URL}/jobs/import`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          jobType: "Import",
          payload: {
            inputFolders: excludedPaths.size === 0 ? [inputDirectory] : [],
            inputFiles: excludedPaths.size > 0
              ? activeCandidates.map(c => c.filePath)
              : undefined,
            outputFolder: outputDirectory,
            importMode: importMode
          }
        }),
      });

      if (!res.ok) {
        throw new Error(await res.text());
      }
      
      const data = await res.json();
      setCurrentJobId(data.jobId);
      importStartTimeRef.current = Date.now();
      setEtaText(null);
      setJobProgress({
        id: data.jobId,
        jobType: "Import",
        status: "Created",
        totalCount: analyzeResult?.totalFiles || 0,
        processedCount: 0,
        errorMessage: null,
        resultPayload: null
      });
      setStep(3);
    } catch (err: any) {
      setError(err.message || "Failed to execute import job");
    } finally {
      setIsExecuting(false);
    }
  };

  const handleCancelJob = async () => {
    if (!currentJobId) return;
    setIsCanceling(true);
    try {
      await fetch(`${API_BASE_URL}/jobs/${currentJobId}/cancel`, {
        method: "POST",
      });
      // Polling will catch the canceled status
    } catch (err) {
      console.error(err);
    } finally {
      setIsCanceling(false);
    }
  };

  const toggleExclude = (filePath: string) => {
    setExcludedPaths(prev => {
      const next = new Set(prev);
      if (next.has(filePath)) next.delete(filePath);
      else next.add(filePath);
      return next;
    });
  };

  const handleSort = (key: typeof sortKey) => {
    if (sortKey === key) setSortAsc(v => !v);
    else { setSortKey(key); setSortAsc(true); }
  };

  const statusOrder = (c: AnalyzeResult["candidates"][0]) =>
    !c.extractedIdentifier ? 2 : c.isExisting ? 1 : 0;

  const sortedCandidates = analyzeResult ? [...analyzeResult.candidates].sort((a, b) => {
    if (!sortKey) return 0;
    let cmp = 0;
    if (sortKey === "fileName") cmp = a.fileName.localeCompare(b.fileName);
    else if (sortKey === "identifier") cmp = (a.extractedIdentifier ?? "").localeCompare(b.extractedIdentifier ?? "");
    else if (sortKey === "status") cmp = statusOrder(a) - statusOrder(b);
    return sortAsc ? cmp : -cmp;
  }) : [];

  const activeCandidates = sortedCandidates.filter(c => !excludedPaths.has(c.filePath));
  const newCount = activeCandidates.filter(c => !c.isExisting && c.extractedIdentifier).length;
  const duplicateCount = activeCandidates.filter(c => c.isExisting).length;
  const unknownCount = activeCandidates.filter(c => !c.extractedIdentifier).length;

  // Parse result payload if completed
  let finalResult = null;
  if (step === 4 && jobProgress?.resultPayload) {
    try {
      finalResult = JSON.parse(jobProgress.resultPayload);
    } catch (e) {}
  }

  return (
    <main className="min-h-screen bg-background text-foreground pb-20">
      <header className="sticky top-0 z-50 w-full border-b bg-background/95 backdrop-blur">
        <div className="container flex h-16 items-center px-4 md:px-6">
          <Link href="/" className="flex items-center gap-2 text-muted-foreground hover:text-foreground transition-colors mr-6">
            <ArrowLeft className="w-4 h-4" /> Back
          </Link>
          <div className="flex items-center gap-2 font-bold text-xl">
            <HardDriveUpload className="w-5 h-5 text-primary" />
            <span>Input Pipeline</span>
          </div>
        </div>
      </header>

      <div className="container max-w-4xl mx-auto py-8 px-4 md:px-6 space-y-8">
        
        {/* Step 1: Input */}
        {step === 1 && (
          <>
            <section className="bg-card/40 border rounded-2xl p-6 shadow-sm">
              <h2 className="text-xl font-bold mb-4 flex items-center gap-2">
                <FolderOpen className="w-5 h-5 text-blue-500" /> Watch Folders
              </h2>
              <p className="text-sm text-muted-foreground mb-4">
                Automatically import media files placed in these directories.
              </p>
              
              <div className="flex gap-2 mb-6">
                <input
                  type="text"
                  placeholder="Add new watch folder path (e.g. D:\Downloads)"
                  value={newWatchFolderPath}
                  onChange={(e) => setNewWatchFolderPath(e.target.value)}
                  className="flex-1 h-10 bg-background border rounded-lg px-4 focus:ring-2 focus:ring-primary/50 focus:outline-none text-sm"
                />
                <button
                  onClick={handleAddWatchFolder}
                  disabled={!newWatchFolderPath}
                  className="h-10 px-4 bg-secondary text-secondary-foreground font-medium rounded-lg hover:bg-secondary/80 disabled:opacity-50 flex items-center gap-2 transition-colors"
                >
                  <Plus className="w-4 h-4" /> Add
                </button>
              </div>

              <div className="space-y-2">
                {watchFolders.map(folder => (
                  <div key={folder.id} className="flex items-center justify-between p-3 border rounded-xl bg-background/50">
                    <div className="flex items-center gap-3 min-w-0">
                      <button
                        onClick={() => handleToggleWatchFolder(folder.id)}
                        className={`p-1.5 rounded-md transition-colors shrink-0 ${folder.isEnabled ? 'bg-emerald-500/10 text-emerald-500 hover:bg-emerald-500/20' : 'bg-muted text-muted-foreground hover:bg-muted/80'}`}
                        title={folder.isEnabled ? "Disable" : "Enable"}
                      >
                        {folder.isEnabled ? <Power className="w-4 h-4" /> : <PowerOff className="w-4 h-4" />}
                      </button>
                      <span className={`font-mono text-sm truncate ${!folder.isEnabled && 'text-muted-foreground line-through'}`}>{folder.path}</span>
                    </div>
                    <div className="flex items-center gap-1 shrink-0">
                      <button
                        onClick={() => openSystemPath(folder.path).catch(() => {})}
                        className="p-1.5 text-muted-foreground hover:text-foreground hover:bg-muted/50 rounded-md transition-colors"
                        title="エクスプローラーで開く"
                      >
                        <FolderOpen className="w-4 h-4" />
                      </button>
                      <button
                        onClick={() => handleDeleteWatchFolder(folder.id)}
                        className="p-1.5 text-destructive/70 hover:text-destructive hover:bg-destructive/10 rounded-md transition-colors"
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </div>
                  </div>
                ))}
                {watchFolders.length === 0 && (
                  <div className="text-center p-6 border border-dashed rounded-xl text-muted-foreground text-sm">
                    No watch folders configured.
                  </div>
                )}
              </div>
            </section>

            <section className="bg-card border rounded-2xl p-6 shadow-sm">
              <h2 className="text-xl font-bold mb-4 flex items-center gap-2">
                <HardDriveUpload className="w-5 h-5 text-emerald-500" /> Manual Import
              </h2>
              <p className="text-sm text-muted-foreground mb-6">
                Analyze and import files from a specific directory.
              </p>

              <div className="space-y-5">
                <div>
                  <label className="block text-sm font-semibold mb-1.5 text-foreground/80">Input Directory</label>
                  <div className="flex gap-2">
                    <input
                      type="text"
                      placeholder="e.g. C:\Downloads"
                      value={inputDirectory}
                      onChange={(e) => setInputDirectory(e.target.value)}
                      className="flex-1 h-11 bg-background border rounded-lg px-4 focus:ring-2 focus:ring-primary/50 focus:outline-none"
                    />
                    <button
                      type="button"
                      onClick={() => browseFolder(setInputDirectory, setBrowsingInput, inputDirectory)}
                      disabled={browsingInput}
                      title="エクスプローラで選択"
                      className="h-11 px-3 bg-secondary text-secondary-foreground rounded-lg hover:bg-secondary/80 disabled:opacity-50 transition-colors flex items-center gap-1.5 shrink-0"
                    >
                      {browsingInput ? <Loader2 className="w-4 h-4 animate-spin" /> : <FolderOpen className="w-4 h-4" />}
                    </button>
                  </div>
                </div>

                <div>
                  <label className="block text-sm font-semibold mb-1.5 text-foreground/80">Output Directory (Library Root)</label>
                  <div className="flex gap-2">
                    <input
                      type="text"
                      placeholder="e.g. D:\WISE_Library"
                      value={outputDirectory}
                      onChange={(e) => setOutputDirectory(e.target.value)}
                      className="flex-1 h-11 bg-background border rounded-lg px-4 focus:ring-2 focus:ring-primary/50 focus:outline-none"
                    />
                    <button
                      type="button"
                      onClick={() => browseFolder(setOutputDirectory, setBrowsingOutput, outputDirectory)}
                      disabled={browsingOutput}
                      title="エクスプローラで選択"
                      className="h-11 px-3 bg-secondary text-secondary-foreground rounded-lg hover:bg-secondary/80 disabled:opacity-50 transition-colors flex items-center gap-1.5 shrink-0"
                    >
                      {browsingOutput ? <Loader2 className="w-4 h-4 animate-spin" /> : <FolderOpen className="w-4 h-4" />}
                    </button>
                  </div>
                </div>

                <div>
                  <label className="block text-sm font-semibold mb-2 text-foreground/80">Import Mode</label>
                  <div className="flex gap-4">
                    <label className="flex items-center gap-2 cursor-pointer">
                      <input 
                        type="radio" 
                        name="importMode" 
                        value="Copy" 
                        checked={importMode === "Copy"} 
                        onChange={() => setImportMode("Copy")} 
                        className="w-4 h-4 text-primary focus:ring-primary"
                      />
                      <span>Copy files</span>
                    </label>
                    <label className="flex items-center gap-2 cursor-pointer">
                      <input 
                        type="radio" 
                        name="importMode" 
                        value="Move" 
                        checked={importMode === "Move"} 
                        onChange={() => setImportMode("Move")} 
                        className="w-4 h-4 text-primary focus:ring-primary"
                      />
                      <span>Move files</span>
                    </label>
                  </div>
                </div>
              </div>
            </section>

            <section className="bg-background border rounded-2xl p-6 shadow-sm flex flex-col items-center justify-center py-10">
              <button
                onClick={handleAnalyze}
                disabled={isAnalyzing || !inputDirectory}
                className="h-14 px-10 text-lg bg-primary text-primary-foreground font-bold rounded-xl hover:bg-primary/90 disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-3 transition-colors shadow-lg shadow-primary/20"
              >
                {isAnalyzing ? <Loader2 className="w-6 h-6 animate-spin" /> : <Play className="w-6 h-6" />}
                <span>Analyze Directory</span>
              </button>
              
              {error && (
                <div className="mt-6 flex items-center gap-2 text-destructive font-medium bg-destructive/10 px-4 py-2 rounded-lg">
                  <ShieldAlert className="w-5 h-5" /> {error}
                </div>
              )}
            </section>
          </>
        )}

        {/* Step 2: Preview */}
        {step === 2 && analyzeResult && (
          <section className="space-y-6">
            <div className="flex items-center gap-4">
              <button onClick={() => { setStep(1); setExcludedPaths(new Set()); }} className="p-2 border rounded-lg hover:bg-muted">
                <ArrowLeft className="w-5 h-5" />
              </button>
              <h2 className="text-2xl font-bold">Analyze Result</h2>
            </div>

            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              <div className="bg-card border rounded-xl p-4 flex flex-col items-center justify-center">
                <div className="text-3xl font-bold text-foreground">{analyzeResult.totalFiles}</div>
                <div className="text-sm text-muted-foreground font-medium">Total Files</div>
              </div>
              <div className="bg-emerald-500/10 border border-emerald-500/20 rounded-xl p-4 flex flex-col items-center justify-center">
                <div className="text-3xl font-bold text-emerald-600 dark:text-emerald-400">{newCount}</div>
                <div className="text-sm text-emerald-600/80 dark:text-emerald-400/80 font-medium flex items-center gap-1">
                  <span className="w-2 h-2 rounded-full bg-emerald-500"></span> New
                </div>
              </div>
              <div className="bg-amber-500/10 border border-amber-500/20 rounded-xl p-4 flex flex-col items-center justify-center">
                <div className="text-3xl font-bold text-amber-600 dark:text-amber-400">{duplicateCount}</div>
                <div className="text-sm text-amber-600/80 dark:text-amber-400/80 font-medium flex items-center gap-1">
                  <span className="w-2 h-2 rounded-full bg-amber-500"></span> Duplicate
                </div>
              </div>
              <div className="bg-rose-500/10 border border-rose-500/20 rounded-xl p-4 flex flex-col items-center justify-center">
                <div className="text-3xl font-bold text-rose-600 dark:text-rose-400">{unknownCount}</div>
                <div className="text-sm text-rose-600/80 dark:text-rose-400/80 font-medium flex items-center gap-1">
                  <span className="w-2 h-2 rounded-full bg-rose-500"></span> Unknown
                </div>
              </div>
            </div>

            <div className="bg-card border rounded-xl overflow-hidden">
              <div className="px-4 py-3 border-b bg-muted/30 font-semibold text-sm flex">
                <div className="w-8 shrink-0" />
                <SortHeader label="File Name" col="fileName" sortKey={sortKey} sortAsc={sortAsc} onSort={handleSort} className="flex-1" />
                <SortHeader label="Identifier" col="identifier" sortKey={sortKey} sortAsc={sortAsc} onSort={handleSort} className="w-40" />
                <SortHeader label="Status" col="status" sortKey={sortKey} sortAsc={sortAsc} onSort={handleSort} className="w-32" />
              </div>
              <div className="max-h-[400px] overflow-y-auto divide-y">
                {sortedCandidates.map((c, i) => {
                  const excluded = excludedPaths.has(c.filePath);
                  return (
                    <div key={i} className={`px-4 py-3 text-sm flex items-center transition-colors ${excluded ? "opacity-40 bg-muted/20" : "hover:bg-muted/10"}`}>
                      <div className="w-8 shrink-0">
                        <button
                          onClick={() => toggleExclude(c.filePath)}
                          title={excluded ? "除外を解除" : "インポートから除外"}
                          className={`w-5 h-5 rounded flex items-center justify-center transition-colors ${excluded ? "bg-muted text-muted-foreground hover:bg-emerald-500/20 hover:text-emerald-500" : "text-muted-foreground/40 hover:text-destructive hover:bg-destructive/10"}`}
                        >
                          {excluded ? <Plus className="w-3 h-3" /> : <XCircle className="w-3.5 h-3.5" />}
                        </button>
                      </div>
                      <div className={`flex-1 font-mono truncate mr-4 ${excluded ? "line-through" : ""}`} title={c.fileName}>{c.fileName}</div>
                      <div className="w-40 font-bold">
                        {c.extractedIdentifier ? c.extractedIdentifier : <span className="text-muted-foreground italic">Unknown</span>}
                      </div>
                      <div className="w-32">
                        {!c.extractedIdentifier ? (
                          <span className="px-2 py-1 rounded-md bg-rose-500/10 text-rose-600 dark:text-rose-400 font-medium text-xs flex items-center gap-1.5 w-fit">
                            <span className="w-1.5 h-1.5 rounded-full bg-rose-500"></span> Unknown
                          </span>
                        ) : c.isExisting ? (
                          <span className="px-2 py-1 rounded-md bg-amber-500/10 text-amber-600 dark:text-amber-400 font-medium text-xs flex items-center gap-1.5 w-fit">
                            <span className="w-1.5 h-1.5 rounded-full bg-amber-500"></span> Duplicate
                          </span>
                        ) : (
                          <span className="px-2 py-1 rounded-md bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 font-medium text-xs flex items-center gap-1.5 w-fit">
                            <span className="w-1.5 h-1.5 rounded-full bg-emerald-500"></span> New
                          </span>
                        )}
                      </div>
                    </div>
                  );
                })}
                {analyzeResult.candidates.length === 0 && (
                  <div className="py-10 text-center text-muted-foreground text-sm">
                    No supported media files found in the directory.
                  </div>
                )}
              </div>
            </div>

            <div className="flex flex-col items-center pt-6">
              <button
                onClick={handleExecute}
                disabled={isExecuting || activeCandidates.length === 0}
                className="h-14 px-10 text-lg bg-primary text-primary-foreground font-bold rounded-xl hover:bg-primary/90 disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-3 transition-colors shadow-lg shadow-primary/20"
              >
                {isExecuting ? <Loader2 className="w-6 h-6 animate-spin" /> : <HardDriveUpload className="w-6 h-6" />}
                <span>Execute Import</span>
              </button>
              
              {error && (
                <div className="mt-6 flex items-center gap-2 text-destructive font-medium bg-destructive/10 px-4 py-2 rounded-lg">
                  <ShieldAlert className="w-5 h-5" /> {error}
                </div>
              )}
            </div>
          </section>
        )}

        {/* Step 3: Progress */}
        {step === 3 && jobProgress && (
          <section className="bg-card border rounded-2xl p-10 shadow-sm flex flex-col items-center justify-center text-center">
            {jobProgress.status === "Failed" || jobProgress.status === "Canceled" ? (
              <div className="w-20 h-20 bg-rose-500/10 rounded-full flex items-center justify-center mb-6">
                <XCircle className="w-10 h-10 text-rose-500" />
              </div>
            ) : (
              <div className="w-20 h-20 bg-primary/10 rounded-full flex items-center justify-center mb-6">
                <RefreshCw className="w-10 h-10 text-primary animate-spin" />
              </div>
            )}
            
            <h2 className="text-3xl font-bold mb-2">
              {jobProgress.status === "Failed" ? "Import Failed" : 
               jobProgress.status === "Canceled" ? "Import Canceled" : 
               jobProgress.status === "Created" || jobProgress.status === "Queued" ? "In Queue..." : 
               "Importing..."}
            </h2>
            <p className="text-muted-foreground mb-8">
              Job #{jobProgress.id.substring(0, 8)}
            </p>

            {(jobProgress.status === "Running" || jobProgress.status === "Created" || jobProgress.status === "Queued") && (
              <div className="w-full max-w-md space-y-4 mb-8">
                {(() => {
                  const total = Math.max(jobProgress.totalCount, analyzeResult?.totalFiles || 0);
                  const pct = total > 0 ? Math.max(0, Math.min(100, Math.round((jobProgress.processedCount / total) * 100))) : 0;
                  return (
                    <>
                      <div className="flex justify-between text-sm font-medium">
                        <span>Progress</span>
                        <span>{jobProgress.processedCount} / {total} 件完了 ({pct}%)</span>
                      </div>
                      <div className="w-full bg-muted rounded-full h-3 overflow-hidden">
                        <div
                          className="bg-primary h-3 rounded-full transition-all duration-500 ease-out"
                          style={{ width: `${pct}%` }}
                        />
                      </div>
                      {etaText && (
                        <p className="text-sm text-muted-foreground text-center">残り{etaText}</p>
                      )}
                    </>
                  );
                })()}
              </div>
            )}

            {/* Scan status chip — shown when metadata jobs are running */}
            {activeJobs.length > 0 && (
              <div className="flex items-center gap-2 px-4 py-2 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 rounded-lg text-sm font-medium mb-6">
                <ScanLine className="w-4 h-4 animate-pulse" />
                スキャン中: {activeJobs.reduce((s, j) => s + j.processedCount, 0)} / {activeJobs.reduce((s, j) => s + j.totalCount, 0)} 件
              </div>
            )}

            {jobProgress.status === "Failed" && (
              <div className="mb-8 p-4 bg-destructive/10 text-destructive rounded-lg max-w-md w-full text-left">
                <div className="font-bold mb-1 flex items-center gap-2"><AlertCircle className="w-4 h-4"/> Error Details</div>
                <div className="text-sm font-mono whitespace-pre-wrap">{jobProgress.errorMessage}</div>
              </div>
            )}

            {jobProgress.status === "Running" || jobProgress.status === "Queued" ? (
              <button
                onClick={handleCancelJob}
                disabled={isCanceling}
                className="h-11 px-6 bg-destructive text-destructive-foreground font-medium rounded-lg hover:bg-destructive/90 disabled:opacity-50 transition-colors flex items-center gap-2"
              >
                {isCanceling ? <Loader2 className="w-4 h-4 animate-spin" /> : <XCircle className="w-4 h-4" />}
                Cancel Import
              </button>
            ) : (jobProgress.status === "Failed" || jobProgress.status === "Canceled") && (
              <div className="flex gap-4">
                <button 
                  onClick={() => { setStep(1); setInputDirectory(""); }}
                  className="h-11 px-6 bg-secondary text-secondary-foreground font-medium rounded-lg hover:bg-secondary/80 transition-colors"
                >
                  Start Over
                </button>
              </div>
            )}
          </section>
        )}

        {/* Step 4: Completed */}
        {step === 4 && finalResult && (
          <section className="bg-card border rounded-2xl p-10 shadow-sm flex flex-col items-center justify-center text-center">
            <div className="w-20 h-20 bg-emerald-500/10 rounded-full flex items-center justify-center mb-6">
              <CheckCircle2 className="w-10 h-10 text-emerald-500" />
            </div>
            <h2 className="text-3xl font-bold mb-2">Import Completed</h2>
            <p className="text-muted-foreground mb-8">
              Job #{jobProgress?.id?.substring(0, 8) || "N/A"} processed successfully.
            </p>

            <div className="grid grid-cols-3 gap-6 mb-10 w-full max-w-md">
              <div className="bg-background border rounded-xl p-4">
                <div className="flex items-center justify-center text-emerald-500 mb-2"><FolderInput className="w-6 h-6" /></div>
                <div className="text-2xl font-bold">{finalResult.WorksAdded || finalResult.worksAdded || 0}</div>
                <div className="text-xs text-muted-foreground font-medium">Works Added</div>
              </div>
              <div className="bg-background border rounded-xl p-4">
                <div className="flex items-center justify-center text-blue-500 mb-2"><FilePlus className="w-6 h-6" /></div>
                <div className="text-2xl font-bold">{finalResult.AssetsAdded || finalResult.assetsAdded || 0}</div>
                <div className="text-xs text-muted-foreground font-medium">Assets Added</div>
              </div>
              <div className="bg-background border rounded-xl p-4">
                <div className="flex items-center justify-center text-amber-500 mb-2"><Copy className="w-6 h-6" /></div>
                <div className="text-2xl font-bold">{finalResult.DuplicatesMerged || finalResult.duplicatesMerged || 0}</div>
                <div className="text-xs text-muted-foreground font-medium">Duplicates Merged</div>
              </div>
            </div>

            <div className="flex gap-4">
              <button 
                onClick={() => { setStep(1); setInputDirectory(""); setAnalyzeResult(null); }}
                className="h-11 px-6 bg-secondary text-secondary-foreground font-medium rounded-lg hover:bg-secondary/80 transition-colors"
              >
                Import More
              </button>
              <Link 
                href="/jobs"
                className="h-11 px-6 bg-secondary text-secondary-foreground font-medium rounded-lg hover:bg-secondary/80 transition-colors flex items-center"
              >
                View Jobs
              </Link>
              <Link 
                href="/"
                className="h-11 px-6 bg-primary text-primary-foreground font-bold rounded-lg hover:bg-primary/90 transition-colors flex items-center shadow-md shadow-primary/20"
              >
                Open Gallery
              </Link>
            </div>
          </section>
        )}

      </div>
    </main>
  );
}

function SortHeader({
  label, col, sortKey, sortAsc, onSort, className,
}: {
  label: string;
  col: "fileName" | "identifier" | "status";
  sortKey: "fileName" | "identifier" | "status" | null;
  sortAsc: boolean;
  onSort: (col: "fileName" | "identifier" | "status") => void;
  className?: string;
}) {
  const active = sortKey === col;
  return (
    <button
      onClick={() => onSort(col)}
      className={`flex items-center gap-1 hover:text-foreground transition-colors text-left ${active ? "text-foreground" : "text-muted-foreground"} ${className ?? ""}`}
    >
      {label}
      {active
        ? sortAsc ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />
        : <ChevronsUpDown className="w-3.5 h-3.5 opacity-40" />}
    </button>
  );
}
