"use client";

import { useWork } from "@/hooks/useWork";
import { getAssetUrl } from "@/lib/api";
import { ArrowLeft, Loader2, PlayCircle, ShieldCheck, Clock, FileVideo } from "lucide-react";
import Image from "next/image";
import Link from "next/link";
import { use } from "react";

export default function WorkDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const resolvedParams = use(params);
  const { data: work, isLoading, isError } = useWork(resolvedParams.id);

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
        <h1 className="text-3xl font-bold mb-4">Work Not Found</h1>
        <Link href="/" className="text-primary hover:underline flex items-center gap-2">
          <ArrowLeft className="w-4 h-4" /> Back to Gallery
        </Link>
      </div>
    );
  }

  // Find cover asset
  const coverAsset = work.assets.find(a => a.originalFilename?.endsWith('.jpg') || a.originalFilename?.endsWith('.png'));
  const coverUrl = getAssetUrl(coverAsset?.id || null) || `https://picsum.photos/seed/${work.id}/1920/1080`;

  // Helper to extract metadata
  const getMeta = (name: string) => work.metadata.find(m => m.fieldName === name && m.isPrimary)?.value;
  const title = getMeta("Title") || work.primaryIdentifier || "Unknown Title";
  const maker = getMeta("Maker") || "Unknown Maker";

  return (
    <main className="min-h-screen bg-background text-foreground relative">
      {/* Background Hero Image (Blurred) */}
      <div className="absolute top-0 left-0 w-full h-[60vh] z-0 overflow-hidden">
        <Image
          src={coverUrl}
          alt="Hero Background"
          fill
          className="object-cover opacity-30 blur-xl scale-110"
          priority
        />
        <div className="absolute inset-0 bg-gradient-to-b from-transparent via-background/80 to-background" />
      </div>

      <div className="relative z-10 container mx-auto px-4 md:px-8 py-8">
        <Link href="/" className="inline-flex items-center gap-2 text-muted-foreground hover:text-foreground transition-colors mb-8 bg-background/50 backdrop-blur px-4 py-2 rounded-full border">
          <ArrowLeft className="w-4 h-4" /> Back to Gallery
        </Link>

        {/* Hero Section */}
        <div className="flex flex-col md:flex-row gap-8 lg:gap-12 mt-4">
          {/* Cover Image */}
          <div className="w-full md:w-1/3 lg:w-1/4 shrink-0 flex flex-col gap-4">
            <div className="relative aspect-[2/3] w-full rounded-xl overflow-hidden shadow-2xl ring-1 ring-white/10">
              <Image
                src={coverUrl}
                alt={title}
                fill
                className="object-cover"
                priority
              />
            </div>
            <button className="w-full flex items-center justify-center gap-2 bg-primary text-primary-foreground hover:bg-primary/90 font-bold py-3 px-4 rounded-xl shadow-lg transition-transform active:scale-95">
              <PlayCircle className="w-5 h-5" /> Play Main Video
            </button>
          </div>

          {/* Info Section */}
          <div className="flex flex-col flex-1 pt-2 md:pt-8">
            <h1 className="text-4xl md:text-5xl lg:text-6xl font-extrabold tracking-tight mb-2 drop-shadow-md">
              {title}
            </h1>
            <div className="flex flex-wrap items-center gap-3 text-sm font-medium text-muted-foreground mb-6">
              <span className="bg-primary/20 text-primary px-2.5 py-0.5 rounded border border-primary/30">
                {work.primaryIdentifier}
              </span>
              <span>{maker}</span>
            </div>

            {/* Metadata Badges */}
            <div className="flex flex-wrap gap-2 mb-8">
              {work.metadata.map((m, i) => (
                <div key={i} className="px-3 py-1 bg-muted/80 backdrop-blur rounded-full text-xs border border-border/50">
                  <span className="text-muted-foreground mr-1">{m.fieldName}:</span>
                  <span className="text-foreground">{m.value}</span>
                </div>
              ))}
            </div>

            {/* Layout for Assets & Diagnostic */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-8 mt-4">
              {/* Assets List */}
              <div className="bg-card/40 backdrop-blur rounded-2xl p-6 border border-border shadow-sm">
                <h3 className="text-lg font-semibold flex items-center gap-2 mb-4 border-b pb-2">
                  <FileVideo className="w-5 h-5 text-primary" /> Associated Assets
                </h3>
                <ul className="space-y-3">
                  {work.assets.map(asset => (
                    <li key={asset.id} className="flex flex-col gap-1 text-sm bg-background/50 p-3 rounded-lg border">
                      <span className="font-medium truncate" title={asset.originalFilename}>{asset.originalFilename}</span>
                      <div className="flex items-center justify-between text-muted-foreground text-xs">
                        <span>{(asset.fileSize / 1024 / 1024).toFixed(2)} MB</span>
                        <span className="font-mono text-[10px]">{asset.sha256?.substring(0, 8) || 'No Hash'}</span>
                      </div>
                    </li>
                  ))}
                </ul>
              </div>

              {/* Diagnostic & History */}
              <div className="space-y-6">
                <div className="bg-card/40 backdrop-blur rounded-2xl p-6 border border-border shadow-sm">
                  <h3 className="text-lg font-semibold flex items-center gap-2 mb-4 border-b pb-2">
                    <ShieldCheck className="w-5 h-5 text-emerald-500" /> Resolution Diagnostic
                  </h3>
                  <div className="flex items-center justify-between mb-4">
                    <span className="text-sm text-muted-foreground">Overall Confidence</span>
                    <span className="text-xl font-bold text-emerald-400">{work.diagnostic.identifierConfidence ?? '--'}%</span>
                  </div>
                  <div className="space-y-2">
                    {work.diagnostic.evidences.map((e, i) => (
                      <div key={i} className="text-xs flex items-center gap-2 bg-background/50 p-2 rounded">
                        <span className="bg-muted px-1.5 py-0.5 rounded text-muted-foreground font-mono">{e.providerId}</span>
                        <span className="flex-1 truncate">{e.rawValue}</span>
                        <span className="text-emerald-400 font-bold">+{e.score}</span>
                      </div>
                    ))}
                  </div>
                </div>

                <div className="bg-card/40 backdrop-blur rounded-2xl p-6 border border-border shadow-sm">
                  <h3 className="text-lg font-semibold flex items-center gap-2 mb-4 border-b pb-2">
                    <Clock className="w-5 h-5 text-blue-500" /> Event History
                  </h3>
                  <div className="space-y-3">
                    {work.history.map((h, i) => (
                      <div key={i} className="text-xs flex flex-col gap-1 border-l-2 border-primary/50 pl-3">
                        <div className="flex justify-between items-center text-muted-foreground">
                          <span className="font-mono">{new Date(h.occurredAt).toLocaleString()}</span>
                          <span className="bg-muted px-1.5 rounded">{h.actor}</span>
                        </div>
                        <p className="font-medium">{h.eventType}</p>
                        <p className="text-muted-foreground">{h.payload}</p>
                      </div>
                    ))}
                  </div>
                </div>
              </div>
            </div>

          </div>
        </div>
      </div>
    </main>
  );
}
