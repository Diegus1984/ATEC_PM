import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { ExternalLink, Plus, Printer, RefreshCw, Trash2 } from "lucide-react"
import { Link } from "react-router-dom"

import { useConfirm } from "@/components/shared/confirm"
import {
  LookupCombobox,
  type LookupComboboxOption,
} from "@/components/shared/lookup-combobox"
import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { PromptDialog } from "@/features/admin/config-sections/config-sections-dialogs"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyTitle,
} from "@/components/ui/empty"
import { Input } from "@/components/ui/input"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { GridScroller } from "@/components/shared/grid-scroller"
import {
  AutoTextarea,
  ChecklistBulkDeleteBar,
  ChecklistColumnsMenu,
  ChecklistColumnsProvider,
  ChecklistContainerCard,
  ChecklistRow,
  type ChecklistRowContainer,
  ChecklistTable,
  deleteSelectedChecklistItems,
  useChecklistColumns,
  useChecklistRowSelection,
} from "@/features/checklist/checklist-shared"
import { printChecklistRows } from "@/features/checklist/checklist-print"
import {
  ChecklistSidebar,
  type ChecklistView,
} from "@/features/checklist/ChecklistSidebar"
import {
  groupNameExists,
  priorityMeta,
  sortChecklistItems,
} from "@/features/checklist/checklist-utils"
import { useDeferredItemOrder } from "@/lib/use-deferred-item-order"
import {
  addChecklistGroup,
  addChecklistInbox,
  assignChecklistInbox,
  deleteChecklistGroup,
  deleteChecklistInbox,
  fetchChecklistBoard,
  fetchChecklistInbox,
  fetchChecklistProjects,
  updateChecklistGroup,
  updateChecklistInbox,
} from "@/lib/api/checklist"
import type {
  ChecklistBoard,
  ChecklistGroup,
  ChecklistInboxItem,
  ChecklistItem,
  ChecklistProject,
  ChecklistProjectLookup,
} from "@/lib/api/types"
import { dateToIso } from "@/lib/date-iso"
import { useChecklistHub } from "@/lib/signalr/use-checklist-hub"
import { notifyError, notifySuccess } from "@/lib/toast"
import { cn } from "@/lib/utils"

const EMPTY_BOARD: ChecklistBoard = { projects: [], groups: [] }

/** Destinazione scelta per un'attività dell'inbox (commessa oppure gruppo). */
type ChecklistAssignTarget = { projectId: number | null; groupId: number | null }

export function ChecklistPage() {
  const queryClient = useQueryClient()

  const boardQuery = useQuery({
    queryKey: ["checklist", "board"],
    queryFn: fetchChecklistBoard,
  })
  const inboxQuery = useQuery({
    queryKey: ["checklist", "inbox"],
    queryFn: fetchChecklistInbox,
  })
  const projectsQuery = useQuery({
    queryKey: ["checklist", "lookups", "projects"],
    queryFn: fetchChecklistProjects,
  })

  const invalidateBoard = () =>
    queryClient.invalidateQueries({ queryKey: ["checklist", "board"] })
  const invalidateInbox = () =>
    queryClient.invalidateQueries({ queryKey: ["checklist", "inbox"] })

  // La board arriva dal gruppo realtime globale; l'inbox è personale (nessun broadcast).
  useChecklistHub(true, invalidateBoard)

  const board = boardQuery.data ?? EMPTY_BOARD
  const allItems = React.useMemo(
    () => [
      ...board.projects.flatMap((p) => p.items),
      ...board.groups.flatMap((g) => g.items),
    ],
    [board]
  )
  const priCount = (p: number) => allItems.filter((i) => i.priority === p).length
  const inboxCount =
    inboxQuery.data?.filter((item) => item.text.trim()).length ?? 0

  const [view, setView] = React.useState<ChecklistView>({ kind: "inbox" })
  const [extraProjectIds, setExtraProjectIds] = React.useState<number[]>([])
  /** «Aggiorna» riordina le righe secondo priorità/data (le modifiche non spostano subito). */
  const [layoutEpoch, setLayoutEpoch] = React.useState(0)

  const handleRefresh = React.useCallback(() => {
    setLayoutEpoch((n) => n + 1)
    void boardQuery.refetch()
  }, [boardQuery])

  return (
    <ChecklistColumnsProvider>
    <div className="flex h-[calc(100vh-7rem)] flex-col gap-4">
      <div className="flex flex-wrap items-start gap-2">
        <div className="min-w-0 flex-1">
          <h1 className="text-xl font-bold tracking-tight">Check list</h1>
          <p className="text-sm text-muted-foreground">
            Attività per commessa e gruppi generici · raccoglitore e generatore di
            check list
          </p>
        </div>
        <NewGroupButton groups={board.groups} onCreated={invalidateBoard} />
        <ChecklistColumnsMenu />
      </div>

      <div className="flex min-h-0 flex-1 overflow-hidden rounded-lg border bg-background">
        <ChecklistSidebar
          view={view}
          onViewChange={setView}
          board={board}
          projectsLookup={projectsQuery.data ?? []}
          inboxCount={inboxCount}
          allCount={allItems.length}
          priCount={priCount}
          onGroupRenamed={invalidateBoard}
          onGroupDeleted={(groupId) => {
            // Se stavo guardando l'attività eliminata torno a «Tutte le attività».
            if (view.kind === "group" && view.groupId === groupId) {
              setView({ kind: "all" })
            }
            void invalidateBoard()
          }}
        />

        <main className="min-w-0 flex-1 overflow-y-auto p-4">
          {boardQuery.isLoading && view.kind !== "inbox" ? (
            <p className="text-sm text-muted-foreground">Caricamento…</p>
          ) : boardQuery.isError && view.kind !== "inbox" ? (
            <PageErrorAlert message={(boardQuery.error as Error).message} />
          ) : (
            <ChecklistMainContent
              view={view}
              board={board}
              inbox={inboxQuery.data ?? []}
              inboxLoading={inboxQuery.isLoading}
              inboxError={
                inboxQuery.isError ? (inboxQuery.error as Error).message : null
              }
              projectsLookup={projectsQuery.data ?? []}
              extraProjectIds={extraProjectIds}
              onExtraProjectIdsChange={setExtraProjectIds}
              onViewChange={setView}
              onBoardMutated={invalidateBoard}
              onInboxMutated={invalidateInbox}
              onAssigned={(target) => {
                invalidateInbox()
                invalidateBoard()
                // Dopo l'assegnazione si apre la tabella di destinazione, così
                // l'attività appena fissata si completa subito nei dettagli.
                if (target.projectId != null) {
                  const projectId = target.projectId
                  setExtraProjectIds((ids) =>
                    ids.includes(projectId) ? ids : [...ids, projectId]
                  )
                  setView({ kind: "project", projectId })
                } else if (target.groupId != null) {
                  setView({ kind: "group", groupId: target.groupId })
                }
              }}
              onRefresh={handleRefresh}
              refreshing={boardQuery.isFetching}
              layoutEpoch={layoutEpoch}
            />
          )}
        </main>
      </div>
    </div>
    </ChecklistColumnsProvider>
  )
}

function ChecklistMainContent({
  view,
  board,
  inbox,
  inboxLoading,
  inboxError,
  projectsLookup,
  extraProjectIds,
  onExtraProjectIdsChange,
  onViewChange,
  onBoardMutated,
  onInboxMutated,
  onAssigned,
  onRefresh,
  refreshing,
  layoutEpoch,
}: {
  view: ChecklistView
  board: ChecklistBoard
  inbox: ChecklistInboxItem[]
  inboxLoading: boolean
  inboxError: string | null
  projectsLookup: ChecklistProjectLookup[]
  extraProjectIds: number[]
  onExtraProjectIdsChange: (ids: number[]) => void
  onViewChange: (view: ChecklistView) => void
  onBoardMutated: () => void
  onInboxMutated: () => void
  onAssigned: (target: ChecklistAssignTarget) => void
  onRefresh: () => void
  refreshing: boolean
  layoutEpoch: number
}) {
  if (view.kind === "inbox") {
    return (
      <>
        <ContentHead
          title="Fissa attività"
          meta={`${inbox.filter((item) => item.text.trim()).length} in attesa di assegnazione`}
        />
        <InboxView
          inbox={inbox}
          loading={inboxLoading}
          error={inboxError}
          groups={board.groups}
          projectsLookup={projectsLookup}
          onInboxMutated={onInboxMutated}
          onAssigned={onAssigned}
        />
      </>
    )
  }

  if (view.kind === "all") {
    return (
      <>
        <ContentHead
          title="Tutte le attività"
          meta={`${board.projects.length + board.groups.length} tabelle · ${board.projects.reduce((n, p) => n + p.items.length, 0) + board.groups.reduce((n, g) => n + g.items.length, 0)} attività`}
        />
        <BoardView
          board={board}
          projectsLookup={projectsLookup}
          extraProjectIds={extraProjectIds}
          onExtraProjectIdsChange={onExtraProjectIdsChange}
          onViewChange={onViewChange}
          onMutated={onBoardMutated}
          onRefresh={onRefresh}
          refreshing={refreshing}
          layoutEpoch={layoutEpoch}
        />
      </>
    )
  }

  if (view.kind === "priority") {
    const pm = priorityMeta(view.priority)
    const priorityItems = [
      ...board.projects.flatMap((p) => p.items),
      ...board.groups.flatMap((g) => g.items),
    ].filter((i) => i.priority === view.priority)
    // P0: il conteggio anticipa la divisione in due tabelle (oggi / critiche).
    const todayIso = dateToIso(new Date())
    const dueToday = priorityItems.filter(
      (i) => (i.dueDate ?? "").slice(0, 10) === todayIso
    ).length
    return (
      <>
        <div className="mb-4 flex flex-wrap items-baseline gap-x-3 gap-y-2">
          <h2 className="text-lg font-semibold tracking-tight">
            {`Priorità ${pm.code} — ${pm.name}`}
          </h2>
          <span className="text-sm text-muted-foreground">
            {view.priority === 0
              ? `${priorityItems.length} attività · ${dueToday} del giorno · ${priorityItems.length - dueToday} critiche`
              : `${priorityItems.length} attività`}
          </span>
          <Button
            variant="outline"
            size="sm"
            className="ml-auto"
            onClick={onRefresh}
            disabled={refreshing}
            title="Riordina le righe secondo priorità e scadenze aggiornate"
          >
            <RefreshCw className={refreshing ? "animate-spin" : ""} />
            Aggiorna
          </Button>
        </div>
        <PriorityView
          board={board}
          priority={view.priority}
          onMutated={onBoardMutated}
          layoutEpoch={layoutEpoch}
        />
      </>
    )
  }

  if (view.kind === "project") {
    // Una commessa senza attività non è nella board: la si costruisce dal lookup, così
    // ogni commessa aperta (anche appena creata) ha comunque la sua tabella vuota.
    const lookupProject = projectsLookup.find((p) => p.id === view.projectId)
    const project =
      board.projects.find((p) => p.projectId === view.projectId) ??
      (lookupProject || extraProjectIds.includes(view.projectId)
        ? ({
            projectId: view.projectId,
            code: lookupProject?.code ?? "",
            title: lookupProject?.title ?? "",
            customerName: "",
            display: lookupProject?.display ?? lookupProject?.code ?? String(view.projectId),
            items: [],
          } satisfies ChecklistProject)
        : null)

    if (!project) {
      return (
        <Empty className="p-8">
          <EmptyHeader>
            <EmptyTitle>Commessa non trovata</EmptyTitle>
            <EmptyDescription>
              Seleziona un&apos;altra voce dalla barra laterale.
            </EmptyDescription>
          </EmptyHeader>
        </Empty>
      )
    }

    return (
      <>
        <ContentHead title={project.display} meta={`${project.items.length} attività`} />
        <BoardToolbar
          board={board}
          projectsLookup={projectsLookup}
          extraProjectIds={extraProjectIds}
          onExtraProjectIdsChange={onExtraProjectIdsChange}
          onViewChange={onViewChange}
          onRefresh={onRefresh}
          refreshing={refreshing}
        />
        <ChecklistContainerCard
          title={
            <div className="flex items-center gap-2">
              <span>{project.display}</span>
              <Link
                to={`/commesse/${project.projectId}/checklist`}
                state={{ fromGlobal: "/checklist" }}
                className="inline-flex items-center gap-1 text-xs font-normal text-muted-foreground hover:text-primary hover:underline ml-2"
              >
                <ExternalLink className="size-3" />
                Apri commessa
              </Link>
            </div>
          }
          count={project.items.length}
          accent="bg-primary"
        >
          <ChecklistTable
            items={project.items}
            container={{ projectId: project.projectId }}
            onMutated={onBoardMutated}
            layoutEpoch={layoutEpoch}
          />
        </ChecklistContainerCard>
      </>
    )
  }

  const group = board.groups.find((g) => g.id === view.groupId)
  if (!group) {
    return (
      <Empty className="p-8">
        <EmptyHeader>
          <EmptyTitle>Gruppo non trovato</EmptyTitle>
          <EmptyDescription>
            Seleziona un&apos;altra voce dalla barra laterale.
          </EmptyDescription>
        </EmptyHeader>
      </Empty>
    )
  }

  return (
    <>
      <ContentHead title={group.name} meta={`${group.items.length} attività`} />
      <BoardToolbar
        board={board}
        projectsLookup={projectsLookup}
        extraProjectIds={extraProjectIds}
        onExtraProjectIdsChange={onExtraProjectIdsChange}
        onViewChange={onViewChange}
        onRefresh={onRefresh}
        refreshing={refreshing}
      />
      <GroupCard
        group={group}
        groups={board.groups}
        onMutated={onBoardMutated}
        onDeleted={() => onViewChange({ kind: "all" })}
        layoutEpoch={layoutEpoch}
      />
    </>
  )
}

function ContentHead({ title, meta }: { title: string; meta?: string }) {
  return (
    <div className="mb-4 flex flex-wrap items-baseline gap-x-3 gap-y-1">
      <h2 className="text-lg font-semibold tracking-tight">{title}</h2>
      {meta ? <span className="text-sm text-muted-foreground">{meta}</span> : null}
    </div>
  )
}

/** Pulsante «Gruppo» dell'intestazione: crea un gruppo generico da qualsiasi vista. */
function NewGroupButton({
  groups,
  onCreated,
}: {
  groups: ChecklistGroup[]
  onCreated: () => void
}) {
  const [open, setOpen] = React.useState(false)

  return (
    <>
      <Button size="sm" onClick={() => setOpen(true)}>
        <Plus />
        Gruppo
      </Button>
      <PromptDialog
        open={open}
        title="Nuovo gruppo"
        label="Nome gruppo"
        onClose={() => setOpen(false)}
        onConfirm={async (name) => {
          const trimmed = name.trim()
          if (groupNameExists(groups, trimmed)) {
            throw new Error("Esiste già un gruppo con questo nome")
          }
          await addChecklistGroup({ name: trimmed })
          setOpen(false)
          onCreated()
          notifySuccess("Gruppo creato")
        }}
      />
    </>
  )
}

// ── Toolbar commesse / gruppi ─────────────────────────────

function BoardToolbar({
  board,
  projectsLookup,
  extraProjectIds,
  onExtraProjectIdsChange,
  onViewChange,
  onRefresh,
  refreshing,
}: {
  board: ChecklistBoard
  projectsLookup: ChecklistProjectLookup[]
  extraProjectIds: number[]
  onExtraProjectIdsChange: (ids: number[]) => void
  onViewChange: (view: ChecklistView) => void
  onRefresh: () => void
  refreshing: boolean
}) {
  const boardProjectIds = new Set(board.projects.map((p) => p.projectId))
  const availableProjects = projectsLookup.filter(
    (p) => !boardProjectIds.has(p.id) && !extraProjectIds.includes(p.id)
  )

  return (
    <div className="mb-4 flex flex-wrap items-center gap-2">
      <Button
        variant="outline"
        size="sm"
        onClick={onRefresh}
        disabled={refreshing}
        title="Riordina le righe secondo priorità e scadenze aggiornate"
      >
        <RefreshCw className={refreshing ? "animate-spin" : ""} />
        Aggiorna
      </Button>
      <LookupCombobox
        options={availableProjects.map((p) => ({ id: p.id, name: p.display }))}
        value={null}
        onValueChange={(id) => {
          if (id == null) return
          onExtraProjectIdsChange([...extraProjectIds, id])
          onViewChange({ kind: "project", projectId: id })
        }}
        placeholder="Aggiungi commessa…"
        searchPlaceholder="Cerca commessa…"
        emptyText="Nessuna commessa disponibile"
        className="h-8 w-56"
      />
    </div>
  )
}

// ── Board: tutte le commesse e gruppi impilati ────────────

function BoardView({
  board,
  projectsLookup,
  extraProjectIds,
  onExtraProjectIdsChange,
  onViewChange,
  onMutated,
  onRefresh,
  refreshing,
  layoutEpoch,
}: {
  board: ChecklistBoard
  projectsLookup: ChecklistProjectLookup[]
  extraProjectIds: number[]
  onExtraProjectIdsChange: (ids: number[]) => void
  onViewChange: (view: ChecklistView) => void
  onMutated: () => void
  onRefresh: () => void
  refreshing: boolean
  layoutEpoch: number
}) {
  const boardProjectIds = new Set(board.projects.map((p) => p.projectId))
  const placeholders: ChecklistProject[] = extraProjectIds
    .filter((id) => !boardProjectIds.has(id))
    .map((id) => {
      const lk = projectsLookup.find((p) => p.id === id)
      return {
        projectId: id,
        code: lk?.code ?? "",
        title: lk?.title ?? "",
        customerName: "",
        display: lk?.display ?? lk?.code ?? String(id),
        items: [],
      }
    })
  const projects = [...board.projects, ...placeholders]
  const isEmpty = projects.length === 0 && board.groups.length === 0

  return (
    <div className="space-y-4">
      <BoardToolbar
        board={board}
        projectsLookup={projectsLookup}
        extraProjectIds={extraProjectIds}
        onExtraProjectIdsChange={onExtraProjectIdsChange}
        onViewChange={onViewChange}
        onRefresh={onRefresh}
        refreshing={refreshing}
      />

      {isEmpty ? (
        <Empty className="p-8">
          <EmptyHeader>
            <EmptyTitle>Nessuna attività</EmptyTitle>
            <EmptyDescription>
              Crea un gruppo generico o aggiungi una commessa per iniziare.
            </EmptyDescription>
          </EmptyHeader>
        </Empty>
      ) : (
        <div className="space-y-4">
          {projects.map((p) => (
            <ChecklistContainerCard
              key={`p${p.projectId}`}
              title={
                <div className="flex items-center gap-2">
                  <span>{p.display}</span>
                  <Link
                    to={`/commesse/${p.projectId}/checklist`}
                    state={{ fromGlobal: "/checklist" }}
                    className="inline-flex items-center gap-1 text-xs font-normal text-muted-foreground hover:text-primary hover:underline ml-2"
                  >
                    <ExternalLink className="size-3" />
                    Apri commessa
                  </Link>
                </div>
              }
              count={p.items.length}
              accent="bg-primary"
            >
              <ChecklistTable
                items={p.items}
                container={{ projectId: p.projectId }}
                onMutated={onMutated}
                layoutEpoch={layoutEpoch}
              />
            </ChecklistContainerCard>
          ))}
          {board.groups.map((g) => (
            <GroupCard
              key={`g${g.id}`}
              group={g}
              groups={board.groups}
              onMutated={onMutated}
              layoutEpoch={layoutEpoch}
            />
          ))}
        </div>
      )}
    </div>
  )
}

function GroupCard({
  group,
  groups,
  onMutated,
  onDeleted,
  layoutEpoch = 0,
}: {
  group: ChecklistGroup
  groups: ChecklistGroup[]
  onMutated: () => void
  onDeleted?: () => void
  layoutEpoch?: number
}) {
  const confirm = useConfirm()
  const [renaming, setRenaming] = React.useState(false)
  const [name, setName] = React.useState(group.name)
  React.useEffect(() => setName(group.name), [group.name])

  async function saveName() {
    setRenaming(false)
    const v = name.trim()
    if (!v || v === group.name) {
      setName(group.name)
      return
    }
    if (groupNameExists(groups, v, group.id)) {
      notifyError(new Error("Esiste già un gruppo con questo nome"))
      setName(group.name)
      return
    }
    try {
      await updateChecklistGroup(group.id, { name: v, rowVersion: group.rowVersion })
      onMutated()
    } catch (err) {
      notifyError(err as Error)
      onMutated()
    }
  }

  async function remove() {
    const ok = await confirm({
      title: "Eliminare il gruppo?",
      description: `Il gruppo "${group.name}" e tutte le sue attività verranno eliminati.`,
      confirmLabel: "Elimina",
    })
    if (!ok) return
    try {
      await deleteChecklistGroup(group.id)
      onMutated()
      onDeleted?.()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  const title = renaming ? (
    <Input
      autoFocus
      value={name}
      onChange={(e) => setName(e.target.value)}
      onBlur={() => void saveName()}
      onKeyDown={(e) => {
        if (e.key === "Enter") {
          e.preventDefault()
          void saveName()
        }
        if (e.key === "Escape") {
          setName(group.name)
          setRenaming(false)
        }
      }}
      className="h-7 w-56"
    />
  ) : (
    <button type="button" className="text-left" onClick={() => setRenaming(true)}>
      {group.name}
    </button>
  )

  return (
    <ChecklistContainerCard
      title={title}
      count={group.items.length}
      accent="bg-slate-400"
      headerExtra={
        <RowActionsMenu
          size="icon-sm"
          triggerClassName="size-8"
          actions={[
            {
              label: "Elimina gruppo",
              icon: Trash2,
              destructive: true,
              onClick: () => void remove(),
            },
          ]}
        />
      }
    >
      <ChecklistTable
        items={group.items}
        container={{ groupId: group.id }}
        onMutated={onMutated}
        layoutEpoch={layoutEpoch}
      />
    </ChecklistContainerCard>
  )
}

// ── Fissa attività (inbox personale) ─────────────────────

function sortInboxProjects(projects: ChecklistProjectLookup[]) {
  return [...projects].sort((a, b) =>
    a.display.localeCompare(b.display, "it", { numeric: true, sensitivity: "base" })
  )
}

function sortInboxGroups(groups: ChecklistGroup[]) {
  return [...groups].sort((a, b) =>
    a.name.localeCompare(b.name, "it", { numeric: true, sensitivity: "base" })
  )
}

function InboxView({
  inbox,
  loading,
  error,
  groups,
  projectsLookup,
  onInboxMutated,
  onAssigned,
}: {
  inbox: ChecklistInboxItem[]
  loading: boolean
  error: string | null
  groups: ChecklistGroup[]
  projectsLookup: ChecklistProjectLookup[]
  onInboxMutated: () => void
  onAssigned: (target: ChecklistAssignTarget) => void
}) {
  const [newGroupAssignNoteId, setNewGroupAssignNoteId] = React.useState<number | null>(null)

  const sortedProjects = React.useMemo(
    () => sortInboxProjects(projectsLookup),
    [projectsLookup]
  )
  const sortedGroups = React.useMemo(() => sortInboxGroups(groups), [groups])
  const visibleInbox = React.useMemo(
    () => inbox.filter((item) => item.text.trim()),
    [inbox]
  )

  return (
    <div className="space-y-4">
      <div className="rounded-lg border border-primary/30 bg-primary/5 p-3 text-sm text-primary">
        Butta giù le attività qui, poi assegnale a una commessa o a un gruppo.
      </div>

      {loading ? (
        <p className="text-sm text-muted-foreground">Caricamento…</p>
      ) : error ? (
        <PageErrorAlert message={error} />
      ) : (
        <GridScroller className="rounded-lg border bg-card">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-12 text-center">#</TableHead>
                <TableHead>Attività da fissare</TableHead>
                <TableHead className="w-72">Assegna a…</TableHead>
                <TableHead className="w-12" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {visibleInbox.map((note, idx) => (
                <InboxRow
                  key={note.id}
                  index={idx + 1}
                  note={note}
                  projects={sortedProjects}
                  groups={sortedGroups}
                  onInboxMutated={onInboxMutated}
                  onAssigned={onAssigned}
                  onRequestNewGroup={() => setNewGroupAssignNoteId(note.id)}
                />
              ))}
              <InboxNewRow onCreated={onInboxMutated} />
            </TableBody>
          </Table>
        </GridScroller>
      )}

      <PromptDialog
        open={newGroupAssignNoteId != null}
        title="Nuova tabella"
        description="Crea la commessa o gruppo a cui assegnare l'attività."
        label="Nome commessa o gruppo"
        confirmLabel="Crea e assegna"
        onClose={() => setNewGroupAssignNoteId(null)}
        onConfirm={async (name) => {
          const noteId = newGroupAssignNoteId
          if (noteId == null) return
          const note = inbox.find((item) => item.id === noteId)
          if (!note?.text.trim()) {
            throw new Error("Scrivi prima l'attività, poi assegnala")
          }
          const trimmed = name.trim()
          if (groupNameExists(groups, trimmed)) {
            throw new Error("Esiste già un gruppo con questo nome")
          }
          const groupId = await addChecklistGroup({ name: trimmed })
          await assignChecklistInbox(noteId, { projectId: null, groupId })
          setNewGroupAssignNoteId(null)
          onAssigned({ projectId: null, groupId })
          notifySuccess(`Attività assegnata a «${trimmed}»`)
        }}
      />
    </div>
  )
}

function InboxNewRow({ onCreated }: { onCreated: () => void }) {
  const [text, setText] = React.useState("")
  const committing = React.useRef(false)
  const inputRef = React.useRef<HTMLInputElement>(null)

  async function commit() {
    const v = text.trim()
    if (!v || committing.current) return
    committing.current = true
    try {
      await addChecklistInbox({ text: v })
      setText("")
      onCreated()
      requestAnimationFrame(() => inputRef.current?.focus())
    } catch (err) {
      notifyError(err as Error)
    } finally {
      committing.current = false
    }
  }

  return (
    <TableRow className="border-0 bg-muted/40 hover:bg-muted/50">
      <TableCell />
      <TableCell colSpan={3} className="py-2">
        <div className="flex items-center gap-2">
          <Plus className="size-4 shrink-0 text-muted-foreground" />
          <input
            ref={inputRef}
            value={text}
            onChange={(e) => setText(e.target.value)}
            onBlur={() => void commit()}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault()
                void commit()
              }
            }}
            placeholder="Nuova attività…"
            className={cn(
              "h-9 w-full min-w-0 rounded-full border border-dashed border-input bg-background px-3 text-sm shadow-xs outline-none transition-[color,box-shadow] placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50"
            )}
          />
        </div>
      </TableCell>
    </TableRow>
  )
}

function InboxRow({
  index,
  note,
  projects,
  groups,
  onInboxMutated,
  onAssigned,
  onRequestNewGroup,
}: {
  index: number
  note: ChecklistInboxItem
  projects: ChecklistProjectLookup[]
  groups: ChecklistGroup[]
  onInboxMutated: () => void
  onAssigned: (target: ChecklistAssignTarget) => void
  onRequestNewGroup: () => void
}) {
  const [text, setText] = React.useState(note.text)
  const hasText = text.trim().length > 0
  React.useEffect(() => setText(note.text), [note.text])

  async function commit() {
    if (text === note.text) return
    try {
      await updateChecklistInbox(note.id, { text })
      onInboxMutated()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  async function remove() {
    try {
      await deleteChecklistInbox(note.id)
      onInboxMutated()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  // Voci «Assegna a…»: id prefissato p:/g: perché commesse e gruppi convivono.
  const assignOptions = React.useMemo<LookupComboboxOption<string>[]>(
    () => [
      ...projects.map((p) => ({
        id: `p:${p.id}`,
        name: p.display,
        group: "Commesse",
      })),
      ...groups.map((g) => ({ id: `g:${g.id}`, name: g.name, group: "Attività" })),
    ],
    [projects, groups]
  )

  async function assign(value: string) {
    if (!text.trim()) {
      notifyError(new Error("Scrivi prima l'attività, poi assegnala"))
      return
    }
    const projectId = value.startsWith("p:") ? Number(value.slice(2)) : null
    const groupId = value.startsWith("g:") ? Number(value.slice(2)) : null
    try {
      await assignChecklistInbox(note.id, { projectId, groupId })
      onAssigned({ projectId, groupId })
      notifySuccess("Attività assegnata")
    } catch (err) {
      notifyError(err as Error)
    }
  }

  return (
    <TableRow className={hasText ? "bg-muted/20" : undefined}>
      <TableCell className="text-center font-mono text-xs text-muted-foreground">
        {index}
      </TableCell>
      <TableCell className="min-w-[16rem]">
        <AutoTextarea
          value={text}
          onChange={setText}
          onCommit={() => void commit()}
          placeholder=""
          className="border-transparent bg-transparent hover:border-input focus-visible:border-input focus-visible:bg-background"
        />
      </TableCell>
      <TableCell>
        <LookupCombobox
          options={assignOptions}
          value={null}
          onValueChange={(v) => v != null && void assign(v)}
          placeholder="Assegna a…"
          searchPlaceholder="Cerca commessa o gruppo…"
          emptyText="Nessuna commessa o gruppo"
          action={{
            label: "Nuova commessa / gruppo…",
            icon: <Plus className="size-3.5" />,
            onSelect: onRequestNewGroup,
          }}
          className={cn(hasText && "border-primary/40 bg-primary/5")}
        />
      </TableCell>
      <TableCell className="w-12 py-1.5 align-middle">
        <div className="flex justify-end">
          <RowActionsMenu
            size="icon-sm"
            triggerClassName="size-8"
            actions={[
              {
                label: "Elimina riga",
                icon: Trash2,
                destructive: true,
                onClick: () => void remove(),
              },
            ]}
          />
        </div>
      </TableCell>
    </TableRow>
  )
}

// ── Vista priorità aggregata su tutte le commesse/gruppi ──

function PriorityView({
  board,
  priority,
  onMutated,
  layoutEpoch,
}: {
  board: ChecklistBoard
  priority: number
  onMutated: () => void
  layoutEpoch: number
}) {
  const rows = React.useMemo(
    () =>
      [
        ...board.projects.flatMap((p) =>
          p.items.map((item) => ({
            item,
            container: {
              label: p.display,
              code: p.code,
              customer: p.customerName,
              title: p.title,
            } satisfies ChecklistRowContainer,
          }))
        ),
        ...board.groups.flatMap((g) =>
          g.items.map((item) => ({
            item,
            container: { label: g.name } satisfies ChecklistRowContainer,
          }))
        ),
      ].filter((r) => r.item.priority === priority),
    [board, priority]
  )

  const pm = priorityMeta(priority)

  // Sezioni P0 congelate fino ad «Aggiorna» (cambio data non sposta tra le due tabelle).
  const [p0SectionById, setP0SectionById] = React.useState<
    Map<number, "today" | "rest">
  >(() => new Map())

  React.useEffect(() => {
    if (priority !== 0) return
    const today = dateToIso(new Date())
    const next = new Map<number, "today" | "rest">()
    for (const r of rows) {
      next.set(
        r.item.id,
        (r.item.dueDate ?? "").slice(0, 10) === today ? "today" : "rest"
      )
    }
    setP0SectionById(next)
  }, [layoutEpoch, priority]) // eslint-disable-line react-hooks/exhaustive-deps -- solo Aggiorna / cambio tab

  React.useEffect(() => {
    if (priority !== 0) return
    setP0SectionById((prev) => {
      const today = dateToIso(new Date())
      const next = new Map(prev)
      let changed = false
      const liveIds = new Set(rows.map((r) => r.item.id))
      for (const id of next.keys()) {
        if (!liveIds.has(id)) {
          next.delete(id)
          changed = true
        }
      }
      for (const r of rows) {
        if (!next.has(r.item.id)) {
          next.set(
            r.item.id,
            (r.item.dueDate ?? "").slice(0, 10) === today ? "today" : "rest"
          )
          changed = true
        }
      }
      return changed ? next : prev
    })
  }, [rows, priority])

  const split = React.useMemo(() => {
    if (priority !== 0) return null
    return {
      today: rows.filter((r) => (p0SectionById.get(r.item.id) ?? "rest") === "today"),
      rest: rows.filter((r) => (p0SectionById.get(r.item.id) ?? "rest") === "rest"),
    }
  }, [rows, priority, p0SectionById])

  if (rows.length === 0) {
    return (
      <Empty className="p-8">
        <EmptyHeader>
          <EmptyTitle>
            Nessuna attività in priorità {pm.code} — {pm.name}
          </EmptyTitle>
          <EmptyDescription>
            Le attività con questo livello compaiono qui da tutte le commesse e i
            gruppi.
          </EmptyDescription>
        </EmptyHeader>
      </Empty>
    )
  }

  if (split) {
    return (
      <div className="space-y-6">
        <PrioritySection
          title="Priorità 0 del giorno"
          description="In scadenza oggi."
          rows={split.today}
          emptyText="Nessuna attività P0 in scadenza oggi."
          onMutated={onMutated}
          layoutEpoch={layoutEpoch}
        />
        <PrioritySection
          title="Priorità 0 critiche"
          description="Tutte le altre P0: scadute, in programma o senza data."
          rows={split.rest}
          emptyText="Nessuna altra attività P0."
          onMutated={onMutated}
          layoutEpoch={layoutEpoch}
        />
      </div>
    )
  }

  return (
    <PriorityTable rows={rows} onMutated={onMutated} layoutEpoch={layoutEpoch} />
  )
}

/** Blocco titolato di una delle due tabelle P0. */
function PrioritySection({
  title,
  description,
  rows,
  emptyText,
  onMutated,
  layoutEpoch,
}: {
  title: string
  description: string
  rows: { item: ChecklistItem; container: ChecklistRowContainer }[]
  emptyText: string
  onMutated: () => void
  layoutEpoch: number
}) {
  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-baseline gap-2">
        <h3 className="text-base font-semibold">{title}</h3>
        <span className="rounded-md border bg-background px-2 py-0.5 font-mono text-xs text-muted-foreground">
          {rows.length}
        </span>
        <span className="text-sm text-muted-foreground">{description}</span>
        <Button
          variant="outline"
          size="sm"
          className="ml-auto self-center"
          disabled={rows.length === 0}
          title="Apre la stampa del browser: da lì «Salva come PDF»"
          onClick={() => printChecklistRows({ title, subtitle: description, rows })}
        >
          <Printer />
          Stampa PDF
        </Button>
      </div>
      {rows.length === 0 ? (
        <p className="rounded-lg border border-dashed px-3 py-6 text-center text-sm text-muted-foreground">
          {emptyText}
        </p>
      ) : (
        <PriorityTable
          rows={rows}
          onMutated={onMutated}
          layoutEpoch={layoutEpoch}
        />
      )}
    </div>
  )
}

function PriorityTable({
  rows,
  onMutated,
  layoutEpoch,
}: {
  rows: { item: ChecklistItem; container: ChecklistRowContainer }[]
  onMutated: () => void
  layoutEpoch: number
}) {
  const confirm = useConfirm()
  const items = React.useMemo(() => rows.map((r) => r.item), [rows])
  const sortItems = React.useCallback(
    (list: ChecklistItem[]) => sortChecklistItems(list, "date"),
    []
  )
  const sorted = useDeferredItemOrder(items, sortItems, layoutEpoch)
  const containerById = React.useMemo(
    () => new Map(rows.map((r) => [r.item.id, r.container])),
    [rows]
  )
  const visibleCols = useChecklistColumns()
  const visibleIds = React.useMemo(() => sorted.map((item) => item.id), [sorted])
  const {
    selectedRowIds,
    toggleRowSelected,
    toggleAllRowsSelected,
    allVisibleSelected,
    someVisibleSelected,
    clearSelection,
  } = useChecklistRowSelection(visibleIds)

  async function deleteSelected() {
    const ids = [...selectedRowIds]
    await deleteSelectedChecklistItems(ids, confirm, () => {
      clearSelection()
      onMutated()
    })
  }

  return (
    <div className="space-y-2">
      <ChecklistBulkDeleteBar
        count={selectedRowIds.size}
        onDelete={() => void deleteSelected()}
      />
      <GridScroller className="rounded-lg border">
      <Table className="border-separate border-spacing-y-1.5">
        <TableHeader>
          <TableRow>
            <TableHead className="w-10 px-0 text-center">
              <Checkbox
                checked={
                  allVisibleSelected
                    ? true
                    : someVisibleSelected
                      ? "indeterminate"
                      : false
                }
                onCheckedChange={(value) =>
                  toggleAllRowsSelected(value === true)
                }
                aria-label="Seleziona tutte le attività visibili"
              />
            </TableHead>
            <TableHead className="w-44">Commessa / Gruppo</TableHead>
            <TableHead>Attività</TableHead>
            {visibleCols.createdAt && (
              <TableHead className="w-52">Inserimento</TableHead>
            )}
            {visibleCols.priority && (
              <TableHead className="w-36">Priorità</TableHead>
            )}
            {visibleCols.dueDate && <TableHead className="w-52">Scadenza</TableHead>}
            {visibleCols.timeLeft && (
              <TableHead className="w-28 text-center">Tempo residuo</TableHead>
            )}
            {visibleCols.status && <TableHead className="w-32">Stato</TableHead>}
            <TableHead className="w-28 text-right">Azioni</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {sorted.map((item) => (
            <ChecklistRow
              key={item.id}
              item={item}
              onMutated={onMutated}
              showContainer
              container={containerById.get(item.id)}
              selected={selectedRowIds.has(item.id)}
              onSelectedChange={(value) => toggleRowSelected(item.id, value)}
            />
          ))}
        </TableBody>
      </Table>
      </GridScroller>
    </div>
  )
}
