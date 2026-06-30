"use client";

import { useState, useEffect } from "react";
import { ArrowLeft, HardDriveUpload, CheckCircle2, XCircle, Clock, Loader2, Play, ChevronDown, ChevronUp, AlertCircle, RefreshCw, StopCircle } from "lucide-react";
import Link from "next/link";
import { API_BASE_URL } from "@/lib/api";
import { formatDateTime, formatTime } from "@/lib/dateUtils";

interface Job {
  id: string;
  jobType: string;
  status: "Created" | "Queued" | "Running" | "Completed" | "Failed" | "Canceled";
  target: string;
  payload: string | null;
  resultPayload: string | null;
  totalCount: number;
  processedCount: number;
  createdAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  errorMessage: string | null;
}

export default function JobsPage() {
  const [jobs, setJobs] = useState<Job[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [expandedJobId, setExpandedJobId] = useState<string | null>(null);

  useEffect(() => {
    fetchJobs();
    const interval = setInterval(fetchJobs, 2000); // Auto refresh every 2 seconds for better progress tracking
    return () => clearInterval(interval);
  }, []);

  const fetchJobs = async () => {
    try {
      const res = await fetch(`${API_BASE_URL}/jobs`);
      if (res.ok) {
        setJobs(await res.json());
      }
    } catch (err) {
      console.error("Failed to fetch jobs", err);
    } finally {
      setIsLoading(false);
    }
  };

  const handleCancelJob = async (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    try {
      await fetch(`${API_BASE_URL}/jobs/${id}/cancel`, { method: "POST" });
      fetchJobs();
    } catch (err) {
      console.error("Failed to cancel job", err);
    }
  };

  const handleRetryJob = async (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    try {
      await fetch(`${API_BASE_URL}/jobs/${id}/retry`, { method: "POST" });
      fetchJobs();
    } catch (err) {
      console.error("Failed to retry job", err);
    }
  };

  const getStatusIcon = (status: Job["status"]) => {
    switch (status) {
      case "Created":
      case "Queued":
        return <Clock className="w-5 h-5 text-blue-400" />;
      case "Running":
        return <Loader2 className="w-5 h-5 text-amber-500 animate-spin" />;
      case "Completed":
        return <CheckCircle2 className="w-5 h-5 text-emerald-500" />;
      case "Failed":
      case "Canceled":
        return <XCircle className="w-5 h-5 text-destructive" />;
      default:
        return <Clock className="w-5 h-5" />;
    }
  };

  const getStatusBg = (status: Job["status"]) => {
    switch (status) {
      case "Created":
      case "Queued":
        return "bg-blue-500/10 border-blue-500/20";
      case "Running":
        return "bg-amber-500/10 border-amber-500/20";
      case "Completed":
        return "bg-emerald-500/10 border-emerald-500/20";
      case "Failed":
      case "Canceled":
        return "bg-destructive/10 border-destructive/20";
      default:
        return "bg-card border-border";
    }
  };

  return (
    <main className="min-h-screen bg-background text-foreground pb-20">
      <header className="sticky top-0 z-50 w-full border-b bg-background/95 backdrop-blur">
        <div className="container flex h-16 items-center px-4 md:px-6">
          <Link href="/" className="flex items-center gap-2 text-muted-foreground hover:text-foreground transition-colors mr-6">
            <ArrowLeft className="w-4 h-4" /> Back
          </Link>
          <div className="flex items-center gap-2 font-bold text-xl">
            <Play className="w-5 h-5 text-primary" />
            <span>Background Jobs</span>
          </div>
        </div>
      </header>

      <div className="container max-w-5xl mx-auto py-8 px-4 md:px-6">
        {isLoading && jobs.length === 0 ? (
          <div className="flex justify-center py-20">
            <Loader2 className="w-8 h-8 animate-spin text-muted-foreground" />
          </div>
        ) : jobs.length === 0 ? (
          <div className="text-center py-20 border border-dashed rounded-2xl text-muted-foreground">
            No jobs found.
            <div className="mt-4">
              <Link href="/import" className="text-primary hover:underline">
                Go to Import Pipeline
              </Link>
            </div>
          </div>
        ) : (
          <div className="space-y-4">
            {jobs.map(job => (
              <div key={job.id} className={`border rounded-xl overflow-hidden transition-colors ${getStatusBg(job.status)}`}>
                <div 
                  className="p-4 flex flex-col md:flex-row md:items-center gap-4 cursor-pointer hover:bg-black/5 dark:hover:bg-white/5 transition-colors"
                  onClick={() => setExpandedJobId(expandedJobId === job.id ? null : job.id)}
                >
                  <div className="flex items-center gap-4 min-w-[200px]">
                    {getStatusIcon(job.status)}
                    <div>
                      <div className="font-bold">{job.jobType}</div>
                      <div className="text-xs text-muted-foreground font-mono">ID: {job.id.substring(0, 8)}</div>
                    </div>
                  </div>

                  <div className="flex-1 space-y-2">
                    {/* Progress Bar for Running/Queued */}
                    {(job.status === "Running" || job.status === "Queued") && (
                      <div className="w-full">
                        <div className="flex justify-between text-xs mb-1 font-medium text-muted-foreground">
                          <span>Progress</span>
                          <span>{job.processedCount} / {job.totalCount}</span>
                        </div>
                        <div className="w-full bg-background/50 rounded-full h-1.5 overflow-hidden">
                          <div 
                            className="bg-primary h-1.5 rounded-full transition-all duration-500"
                            style={{ width: `${Math.max(0, Math.min(100, (job.processedCount / Math.max(1, job.totalCount)) * 100))}%` }}
                          ></div>
                        </div>
                      </div>
                    )}
                    <div className="text-xs text-muted-foreground">
                      Created: {formatDateTime(job.createdAt)}
                      {job.startedAt && ` • Started: ${formatTime(job.startedAt)}`}
                      {job.finishedAt && ` • Finished: ${formatTime(job.finishedAt)}`}
                    </div>
                  </div>

                  <div className="flex items-center justify-end gap-3 md:min-w-[180px]">
                    {(job.status === "Running" || job.status === "Queued") && (
                      <button 
                        onClick={(e) => handleCancelJob(job.id, e)}
                        className="p-1.5 text-destructive hover:bg-destructive/10 rounded-md transition-colors"
                        title="Cancel Job"
                      >
                        <StopCircle className="w-5 h-5" />
                      </button>
                    )}
                    {(job.status === "Failed" || job.status === "Canceled") && (
                      <button 
                        onClick={(e) => handleRetryJob(job.id, e)}
                        className="p-1.5 text-blue-500 hover:bg-blue-500/10 rounded-md transition-colors"
                        title="Retry Job"
                      >
                        <RefreshCw className="w-5 h-5" />
                      </button>
                    )}

                    <span className={`px-3 py-1 rounded-full text-xs font-bold
                      ${job.status === "Completed" ? "bg-emerald-500/20 text-emerald-500" :
                        job.status === "Running" ? "bg-amber-500/20 text-amber-500" :
                        job.status === "Failed" ? "bg-destructive/20 text-destructive" :
                        "bg-blue-500/20 text-blue-400"
                      }`}>
                      {job.status}
                    </span>
                    
                    <button className="text-muted-foreground ml-2">
                      {expandedJobId === job.id ? <ChevronUp className="w-5 h-5" /> : <ChevronDown className="w-5 h-5" />}
                    </button>
                  </div>
                </div>

                {/* Expanded Details */}
                {expandedJobId === job.id && (
                  <div className="p-4 bg-background/40 border-t border-border/50 text-sm">
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                      <div>
                        <h4 className="font-bold text-xs text-muted-foreground uppercase tracking-wider mb-2">Payload</h4>
                        <pre className="bg-background/80 p-3 rounded-lg overflow-x-auto text-xs font-mono text-muted-foreground">
                          {job.payload ? JSON.stringify(JSON.parse(job.payload), null, 2) : "N/A"}
                        </pre>
                      </div>
                      
                      {job.resultPayload && (
                        <div>
                          <h4 className="font-bold text-xs text-muted-foreground uppercase tracking-wider mb-2">Result</h4>
                          <pre className="bg-emerald-500/10 border border-emerald-500/20 p-3 rounded-lg overflow-x-auto text-xs font-mono text-emerald-600 dark:text-emerald-400">
                            {JSON.stringify(JSON.parse(job.resultPayload), null, 2)}
                          </pre>
                        </div>
                      )}

                      {job.errorMessage && (
                        <div className="md:col-span-2">
                          <h4 className="font-bold text-xs text-muted-foreground uppercase tracking-wider mb-2">Error Log</h4>
                          <div className="bg-destructive/10 border border-destructive/20 p-3 rounded-lg flex gap-3 text-destructive">
                            <AlertCircle className="w-5 h-5 shrink-0" />
                            <pre className="overflow-x-auto text-xs font-mono whitespace-pre-wrap">
                              {job.errorMessage}
                            </pre>
                          </div>
                        </div>
                      )}
                    </div>
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </main>
  );
}
