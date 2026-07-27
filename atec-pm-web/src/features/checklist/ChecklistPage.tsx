import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { ExternalLink, Plus, RefreshCw, Trash2 } from "lucide-react"
import { Link } from "react-router-dom"

import { useConfirm } from "@/components/shared/confirm"
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
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  AutoTextarea,
  ChecklistBulkDeleteBar,
  ChecklistContainerCard,
  ChecklistRow,
  ChecklistTable,
  deleteSelectedChecklistItems,
  useChecklistRowSelection,
} from "@/features/checklist/checklist-shared"
import {
  ChecklistSidebar,
  type ChecklistView,
} from "@/features/checklist/ChecklistSidebar"
import {
  priorityMeta,
  sortChecklistItems,
} from "@/features/checklist/checklist-utils"
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
  ChecklistProject,
  ChecklistProjectLookup,
} from "@/lib/api/types"
import { useChecklistHub } from "@/lib/signalr/use-checklist-hub"
import { notifyError, notifySuccess } from "@/lib/toast"
import { cn } from "@/lib/utils"

const EMPTY_BOARD: ChecklistBoard = { projects: [], groups: [] }

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

  return (
    <div className="flex h-[calc(100vh-7rem)] flex-col gap-4">
      <div>
        <h1 className="text-xl font-bold tracking-tight">Check list</h1>
        <p className="text-sm text-muted-foreground">
          Attività per commessa e gruppi generici · raccoglitore e generatore di
          check list
        </p>
      </div>

      <div className="flex min-h-0 flex-1 overflow-hidden rounded-lg border bg-background">
        <ChecklistSidebar
          view={view}
          onViewChange={setView}
          board={board}
          inboxCount={inboxCount}
          allCount={allItems.length}
          priCount={priCount}
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
              onAssigned={() => {
                invalidateInbox()
                invalidateBoard()
              }}
              onRefresh={() => boardQuery.refetch()}
              refreshing={boardQuery.isFetching}
            />
          )}
        </main>
      </div>
    </div>
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
  onAssigned: () => void
  onRefresh: () => void
  refreshing: boolean
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
        />
      </>
    )
  }

  if (view.kind === "priority") {
    const pm = priorityMeta(view.priority)
    return (
      <>
        <ContentHead
          title={`Priorità ${pm.code} — ${pm.name}`}
          meta={`${board.projects.flatMap((p) => p.items).filter((i) => i.priority === view.priority).length + board.groups.flatMap((g) => g.items).filter((i) => i.priority === view.priority).length} attività`}
        />
        <PriorityView board={board} priority={view.priority} onMutated={onBoardMutated} />
      </>
    )
  }

  if (view.kind === "project") {
    const project =
      board.projects.find((p) => p.projectId === view.projectId) ??
      (extraProjectIds.includes(view.projectId)
        ? (() => {
            const lk = projectsLookup.find((p) => p.id === view.projectId)
            return {
              projectId: view.projectId,
              code: lk?.code ?? "",
              title: lk?.title ?? "",
              display: lk?.display ?? lk?.code ?? String(view.projectId),
              items: [],
            } satisfies ChecklistProject
          })()
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
          onMutated={onBoardMutated}
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
        onMutated={onBoardMutated}
        onRefresh={onRefresh}
        refreshing={refreshing}
      />
      <GroupCard
        group={group}
        groups={board.groups}
        onMutated={onBoardMutated}
        onDeleted={() => onViewChange({ kind: "all" })}
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

function groupNameExists(groups: ChecklistGroup[], name: string, excludeId?: number) {
  const normalized = name.trim().toLowerCase()
  return groups.some(
    (g) =>
      g.id !== excludeId &&
      g.name.trim().toLowerCase() === normalized
  )
}

// ── Toolbar commesse / gruppi ─────────────────────────────

function BoardToolbar({
  board,
  projectsLookup,
  extraProjectIds,
  onExtraProjectIdsChange,
  onViewChange,
  onMutated,
  onRefresh,
  refreshing,
}: {
  board: ChecklistBoard
  projectsLookup: ChecklistProjectLookup[]
  extraProjectIds: number[]
  onExtraProjectIdsChange: (ids: number[]) => void
  onViewChange: (view: ChecklistView) => void
  onMutated: () => void
  onRefresh: () => void
  refreshing: boolean
}) {
  const [addGroupOpen, setAddGroupOpen] = React.useState(false)

  const boardProjectIds = new Set(board.projects.map((p) => p.projectId))
  const availableProjects = projectsLookup.filter(
    (p) => !boardProjectIds.has(p.id) && !extraProjectIds.includes(p.id)
  )

  return (
    <div className="mb-4 flex flex-wrap items-center gap-2">
      <Button variant="outline" size="sm" onClick={onRefresh} disabled={refreshing}>
        <RefreshCw className={refreshing ? "animate-spin" : ""} />
        Aggiorna
      </Button>
      <Button size="sm" onClick={() => setAddGroupOpen(true)}>
        <Plus />
        Gruppo
      </Button>
      <Select
        value=""
        onValueChange={(v) => {
          const id = Number(v)
          onExtraProjectIdsChange([...extraProjectIds, id])
          onViewChange({ kind: "project", projectId: id })
        }}
      >
        <SelectTrigger className="h-8 w-56">
          <SelectValue placeholder="Aggiungi commessa…" />
        </SelectTrigger>
        <SelectContent>
          {availableProjects.length === 0 ? (
            <div className="px-2 py-1.5 text-sm text-muted-foreground">
              Nessuna commessa disponibile
            </div>
          ) : (
            availableProjects.map((p) => (
              <SelectItem key={p.id} value={String(p.id)}>
                {p.display}
              </SelectItem>
            ))
          )}
        </SelectContent>
      </Select>

      <PromptDialog
        open={addGroupOpen}
        title="Nuovo gruppo"
        label="Nome gruppo"
        onClose={() => setAddGroupOpen(false)}
        onConfirm={async (name) => {
          const trimmed = name.trim()
          if (groupNameExists(board.groups, trimmed)) {
            throw new Error("Esiste già un gruppo con questo nome")
          }
          const id = await addChecklistGroup({ name: trimmed })
          setAddGroupOpen(false)
          onMutated()
          onViewChange({ kind: "group", groupId: id })
          notifySuccess("Gruppo creato")
        }}
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
}: {
  board: ChecklistBoard
  projectsLookup: ChecklistProjectLookup[]
  extraProjectIds: number[]
  onExtraProjectIdsChange: (ids: number[]) => void
  onViewChange: (view: ChecklistView) => void
  onMutated: () => void
  onRefresh: () => void
  refreshing: boolean
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
        onMutated={onMutated}
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
              />
            </ChecklistContainerCard>
          ))}
          {board.groups.map((g) => (
            <GroupCard key={`g${g.id}`} group={g} groups={board.groups} onMutated={onMutated} />
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
}: {
  group: ChecklistGroup
  groups: ChecklistGroup[]
  onMutated: () => void
  onDeleted?: () => void
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
  onAssigned: () => void
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
        <div className="overflow-hidden rounded-lg border bg-card">
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
        </div>
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
          onAssigned()
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
  onAssigned: () => void
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

  async function assign(value: string) {
    if (value === "__new__") {
      onRequestNewGroup()
      return
    }
    if (!text.trim()) {
      notifyError(new Error("Scrivi prima l'attività, poi assegnala"))
      return
    }
    const projectId = value.startsWith("p:") ? Number(value.slice(2)) : null
    const groupId = value.startsWith("g:") ? Number(value.slice(2)) : null
    try {
      await assignChecklistInbox(note.id, { projectId, groupId })
      onAssigned()
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
        />
      </TableCell>
      <TableCell>
        <Select value="" onValueChange={(v) => void assign(v)}>
          <SelectTrigger
            className={cn(
              "h-9 w-full",
              hasText && "border-primary/40 bg-primary/5"
            )}
          >
            <SelectValue placeholder="Assegna a…" />
          </SelectTrigger>
          <SelectContent>
            {projects.length > 0 ? (
              <SelectGroup>
                <SelectLabel>Commesse</SelectLabel>
                {projects.map((p) => (
                  <SelectItem key={`p${p.id}`} value={`p:${p.id}`}>
                    {p.display}
                  </SelectItem>
                ))}
              </SelectGroup>
            ) : null}
            {groups.length > 0 ? (
              <SelectGroup>
                <SelectLabel>Attività</SelectLabel>
                {groups.map((g) => (
                  <SelectItem key={`g${g.id}`} value={`g:${g.id}`}>
                    {g.name}
                  </SelectItem>
                ))}
              </SelectGroup>
            ) : null}
            <SelectItem value="__new__">
              <span className="flex items-center gap-2 text-primary">
                <Plus className="size-3.5" />
                Nuova commessa / gruppo…
              </span>
            </SelectItem>
          </SelectContent>
        </Select>
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
}: {
  board: ChecklistBoard
  priority: number
  onMutated: () => void
}) {
  const confirm = useConfirm()
  const rows = React.useMemo(
    () =>
      [
        ...board.projects.flatMap((p) =>
          p.items.map((item) => ({ item, label: p.display }))
        ),
        ...board.groups.flatMap((g) =>
          g.items.map((item) => ({ item, label: g.name }))
        ),
      ].filter((r) => r.item.priority === priority),
    [board, priority]
  )

  const sorted = React.useMemo(
    () => sortChecklistItems(
      rows.map((r) => r.item),
      "date"
    ),
    [rows]
  )
  const labelById = React.useMemo(
    () => new Map(rows.map((r) => [r.item.id, r.label])),
    [rows]
  )
  const pm = priorityMeta(priority)
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

  return (
    <div className="space-y-2">
      <ChecklistBulkDeleteBar
        count={selectedRowIds.size}
        onDelete={() => void deleteSelected()}
      />
      <div className="overflow-hidden rounded-lg border">
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
            <TableHead className="w-56">Commessa / Gruppo</TableHead>
            <TableHead>Attività</TableHead>
            <TableHead className="w-52">Inserimento</TableHead>
            <TableHead className="w-36">Priorità</TableHead>
            <TableHead className="w-52">Scadenza</TableHead>
            <TableHead className="w-28 text-center">Tempo residuo</TableHead>
            <TableHead className="w-32">Stato</TableHead>
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
              containerLabel={labelById.get(item.id)}
              selected={selectedRowIds.has(item.id)}
              onSelectedChange={(value) => toggleRowSelected(item.id, value)}
            />
          ))}
        </TableBody>
      </Table>
      </div>
    </div>
  )
}
