"use client";

import Image from "next/image";
import { motion } from "framer-motion";
import Link from "next/link";
import { getAssetUrl, type WorkItem } from "@/lib/api";
import { useGalleryStore } from "@/store/useGalleryStore";

interface WorkCardProps {
  work: WorkItem;
  style?: React.CSSProperties;
}

export function WorkCard({ work, style }: WorkCardProps) {
  const cardSize = useGalleryStore((state) => state.cardSize);
  const coverUrl = getAssetUrl(work.coverAssetId) || `https://picsum.photos/seed/${work.id}/400/600`;

  // Aspect ratio is roughly 2:3 for standard covers (DVD/Book)
  return (
    <Link href={`/works/${work.id}`} style={style} className="p-2 block">
      <motion.div
        whileHover={{ scale: 1.05, zIndex: 10 }}
        transition={{ type: "spring", stiffness: 300, damping: 20 }}
        className="relative w-full h-full rounded-lg overflow-hidden bg-muted shadow-md group cursor-pointer"
      >
        <Image
          src={coverUrl}
          alt={work.title || work.primaryIdentifier || "Unknown"}
          fill
          sizes="(max-width: 768px) 50vw, (max-width: 1200px) 25vw, 20vw"
          className="object-cover transition-transform duration-500 group-hover:scale-105"
          loading="lazy"
        />
        
        {/* Gradient Overlay for Text */}
        <div className="absolute inset-0 bg-gradient-to-t from-black/80 via-black/0 to-black/0 opacity-0 group-hover:opacity-100 transition-opacity duration-300 flex flex-col justify-end p-4">
          <h3 className="text-white font-bold text-sm line-clamp-2 shadow-sm drop-shadow-md">
            {work.title || work.primaryIdentifier}
          </h3>
          <p className="text-white/80 text-xs mt-1">
            {work.primaryIdentifier}
          </p>
        </div>
      </motion.div>
    </Link>
  );
}
