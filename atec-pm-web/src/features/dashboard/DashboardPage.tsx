import { useQuery } from "@tanstack/react-query"

import { Skeleton } from "@/components/ui/skeleton"
import { DashboardHoursChart } from "@/features/dashboard/components/DashboardHoursChart"
import { DashboardProjectsTable } from "@/features/dashboard/components/DashboardProjectsTable"
import { DashboardSectionCards } from "@/features/dashboard/components/DashboardSectionCards"
import { fetchDashboard } from "@/lib/api/dashboard"

function DashboardSkeleton() {
  return (
    <div className="flex flex-col gap-4 md:gap-6">
      <div className="grid grid-cols-1 gap-4 @xl/main:grid-cols-2 @5xl/main:grid-cols-4">
        {Array.from({ length: 4 }).map((_, index) => (
          <Skeleton key={index} className="h-36 rounded-xl" />
        ))}
      </div>
      <Skeleton className="h-80 rounded-xl" />
      <Skeleton className="h-64 rounded-xl" />
    </div>
  )
}

export function DashboardPage() {
  const query = useQuery({
    queryKey: ["dashboard"],
    queryFn: fetchDashboard,
  })

  if (query.isLoading) {
    return <DashboardSkeleton />
  }

  if (query.isError) {
    return (
      <p className="text-sm text-destructive">
        {(query.error as Error).message || "Errore dashboard"}
      </p>
    )
  }

  const data = query.data!

  return (
    <div className="flex flex-col gap-4 md:gap-6">
      <DashboardSectionCards data={data} />
      <DashboardHoursChart dailyHours={data.dailyHours ?? []} />
      <DashboardProjectsTable projects={data.recentProjects} />
    </div>
  )
}
