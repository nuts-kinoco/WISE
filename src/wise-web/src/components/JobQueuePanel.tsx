"use client";

import { useEffect, useState } from "react";
import { Loader2, CheckCircle2, XCircle, Clock, ChevronDown, ChevronUp } from "lucide-react";
import { API_BASE_URL } from "@/lib/api";

interface ActiveJob {
  id: string;
  jobType: string;
  status: string;
  identifier: string | null;
  target: string | null;
  createdAt: string;
  startedAt: string | null;
  errorMessage: string | null;
}

const JOB_LABELS: Record<string, string> = {
  Import: "インポート",
  FetchMetadata: "スクレイピング",
};

const STATUS_ICON = {
  Created: <Clock className="w-3.5 h-3.5 text-muted-foreground" />,
  Queued: <Clock className="w-3.5 h-3.5 text-amber-400" />,
  Running: <Loader2 className="w-3.5 h-3.5 text-primary animate-spin" />,
  Completed: <CheckCircle2 className="w-3.5 h-3.5 text-emerald-400" />,
  Failed: <XCircle className="w-3.5 h-3.5 text-destructive" />,
  Canceled: <XCircle className="w-3.5 h-3.5 text-muted-foreground" />,
} as Record<string, React.ReactNode>;

export function useActiveJobs() {
  const [jobs, setJobs] = useState<ActiveJob[]>([]);
  const [hasActive, setHasActive] = useState(false);

  useEffect(() => {
    let timer: ReturnType<typeof setTimeout>;

    const poll = async () => {
      try {
        const res = await fetch(`${API_BASE_URL}/jobs/active`);
        if (res.ok) {
          const data: ActiveJob[] = await res.json();
          setJobs(data);
          setHasActive(data.length > 0);
        }
      } catch {}
      timer = setTimeout(poll, 2000);
    };

    poll();
    return () => clearTimeout(timer);
  }, []);

  return { jobs, hasActive };
}

export function JobQueuePanel() {
  const { jobs, hasActive } = useActiveJobs();
  const [collapsed, setCollapsed] = useState(false);

  if (!hasActive) return null;

  const running = jobs.filter(j => j.status === "Running").length;
  const queued = jobs.filter(j => j.status === "Queued" || j.status === "Created").length;

  return (
    <div className="fixed bottom-4 right-4 z-50 w-80 bg-card border border-border/60 rounded-2xl shadow-2xl overflow-hidden">
      {/* Header */}
      <button
        onClick={() => setCollapsed(v => !v)}
        className="w-full flex items-center justify-between px-4 py-3 bg-card/80 hover:bg-muted/30 transition-colors"
      >
        <div className="flex items-center gap-2">
          <Loader2 className="w-4 h-4 text-primary animate-spin" />
          <span className="text-sm font-semibold">処理中</span>
          <span className="text-xs text-muted-foreground">
            実行中 {running} / 待機 {queued}
          </span>
        </div>
        {collapsed ? <ChevronUp className="w-4 h-4 text-muted-foreground" /> : <ChevronDown className="w-4 h-4 text-muted-foreground" />}
      </button>

      {/* Job list */}
      {!collapsed && (
        <div className="max-h-60 overflow-y-auto divide-y divide-border/30">
          {jobs.map(job => (
            <div key={job.id} className="flex items-center gap-3 px-4 py-2.5">
              <div className="shrink-0">{STATUS_ICON[job.status] ?? STATUS_ICON.Queued}</div>
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <span className="text-xs font-mono font-bold text-primary/80 truncate">
                    {job.identifier ?? job.target ?? "—"}
                  </span>
                </div>
                <div className="text-[10px] text-muted-foreground">
                  {JOB_LABELS[job.jobType] ?? job.jobType}
                  {job.status === "Running" && " 中..."}
                  {job.status === "Queued" && " (待機中)"}
                  {job.status === "Created" && " (待機中)"}
                </div>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export function TopProgressBar() {
  const { jobs } = useActiveJobs();
  const hasActive = jobs.length > 0;

  if (!hasActive) return null;

  return (
    <div className="fixed top-0 left-0 right-0 z-[60] h-0.5 bg-primary/20 overflow-hidden">
      <div className="h-full bg-primary animate-[progress-slide_1.5s_ease-in-out_infinite]" style={{ width: "40%" }} />
    </div>
  );
}
