import { cn } from "@/lib/utils"

const ATEC_ICON_SRC = "/atec-icon.png"

interface AtecBrandIconProps {
  className?: string
  size?: "sm" | "md" | "lg"
}

const sizeClasses = {
  sm: "size-8",
  md: "size-10",
  lg: "size-12",
} as const

export function AtecBrandIcon({ className, size = "sm" }: AtecBrandIconProps) {
  return (
    <div
      className={cn(
        "flex shrink-0 items-center justify-center overflow-hidden rounded-lg bg-white",
        sizeClasses[size],
        className
      )}
    >
      <img
        src={ATEC_ICON_SRC}
        alt="ATEC"
        className="size-full object-contain p-0.5"
        draggable={false}
      />
    </div>
  )
}

export function AtecLogoWordmark({ className }: { className?: string }) {
  return (
    <img
      src="/atec-logo.png"
      alt="Automation Technology S.r.l."
      className={cn("h-8 w-auto max-w-[140px] object-contain object-left", className)}
      draggable={false}
    />
  )
}
