import * as React from "react"
import {
  ChevronDown,
  ChevronUp,
  Copy,
  Info,
  Loader2,
  Paperclip,
  X,
} from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { useCopyText } from "@/components/shared/copy-text"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Collapsible } from "@/components/ui/collapsible"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import {
  createBugReport,
  deleteBugAttachment,
  updateBugReport,
  updateBugReportStatus,
  uploadBugAttachment,
} from "@/lib/api/bug-reports"
import { formatLastErrorForContext } from "@/lib/api/last-error"
import type {
  BugAttachment,
  BugKind,
  BugReport,
  BugSeverity,
  BugStatus,
} from "@/lib/api/types"
import { APP_BUILD } from "@/lib/app-version"
import { getSession } from "@/lib/auth/session"
import { formatDateTimeShort } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"
import { cn } from "@/lib/utils"

import { BugAttachmentThumb } from "./BugAttachmentThumb"
import { STATUS_META, STATUS_ORDER, buildBugMarkdown, formatSize } from "./bug-report-utils"

/**
 * Dialog unico per aprire, leggere e gestire una segnalazione.
 * Supporta:
 * - Incolla screenshot da clipboard (Ctrl+V) - L1
 * - Cattura contesto tecnico automatico (rotta, build, browser, viewport, errore API) - L2
 * - Copia per analisi Markdown (BUG-NNN) - L6
 * - Visualizzazione build di risoluzione - L7
 * - Gestione allegati separati per la risposta amministratore - L9
 */
export function BugReportDialog({
  open,
  bug,
  initialTitle,
  initialContext,
  isAdmin,
  canWrite = true,
  onClose,
  onSaved,
}: {
  open: boolean
  /** null = nuova segnalazione. */
  bug: BugReport | null
  initialTitle?: string
  initialContext?: string
  isAdmin: boolean
  /** Funzione concessa in sola lettura (tecnico del reparto Contabilità): tutto bloccato. */
  canWrite?: boolean
  onClose: () => void
  onSaved: () => void
}) {
  const confirm = useConfirm()
  const copiaTesto = useCopyText()
  const isNew = bug == null
  const canEditContent = canWrite && (isNew || bug.isMine || isAdmin)
  const myEmployeeId = getSession()?.user.employeeId ?? 0

  function canDeleteAttachment(attachment: BugAttachment): boolean {
    if (!canWrite) return false
    if (attachment.isReply) {
      return isAdmin
    }
    if (isAdmin) return true
    return myEmployeeId > 0 && attachment.createdById === myEmployeeId
  }

  const [kind, setKind] = React.useState<BugKind>("BUG")
  const [title, setTitle] = React.useState("")
  const [description, setDescription] = React.useState("")
  const [area, setArea] = React.useState("")
  const [severity, setSeverity] = React.useState<BugSeverity>("MEDIUM")
  const [status, setStatus] = React.useState<BugStatus>("OPEN")
  const [adminNote, setAdminNote] = React.useState("")
  const [context, setContext] = React.useState("")
  const [contextOpen, setContextOpen] = React.useState(false)

  const [pendingFiles, setPendingFiles] = React.useState<File[]>([])
  const [pendingReplyFiles, setPendingReplyFiles] = React.useState<File[]>([])
  const [saving, setSaving] = React.useState(false)

  const fileRef = React.useRef<HTMLInputElement>(null)
  const replyFileRef = React.useRef<HTMLInputElement>(null)
  const isTargetingReply = React.useRef<boolean>(false)

  // Composizione contesto tecnico automatico per nuove segnalazioni
  const generateAutoContext = React.useCallback(() => {
    const route = window.location.pathname + window.location.search
    const viewport = `${window.innerWidth}x${window.innerHeight}`
    const lastErr = formatLastErrorForContext()

    const parts = [
      `Rotta: ${route}`,
      `Build: ${APP_BUILD}`,
      `Viewport: ${viewport}`,
      `User-Agent: ${navigator.userAgent}`,
    ]

    if (lastErr) {
      parts.push(`\n--- Ultimo errore API rilevato ---\n${lastErr}`)
    }

    return parts.join("\n")
  }, [])

  // Riallinea il form a ogni apertura
  React.useEffect(() => {
    if (!open) return
    setKind(bug?.kind ?? "BUG")
    setTitle(bug?.title ?? initialTitle ?? "")
    setDescription(bug?.description ?? "")
    setArea(bug?.area ?? "")
    setSeverity(bug?.severity ?? "MEDIUM")
    setStatus(bug?.status ?? "OPEN")
    setAdminNote(bug?.adminNote ?? "")
    setContext(bug?.context ?? initialContext ?? (isNew ? generateAutoContext() : ""))
    setContextOpen(false)
    setPendingFiles([])
    setPendingReplyFiles([])
    setSaving(false)
  }, [open, bug, isNew, initialTitle, initialContext, generateAutoContext])

  // L1: Incolla screenshot da clipboard con Ctrl+V
  function handlePaste(event: React.ClipboardEvent) {
    const items = event.clipboardData?.items
    if (!items) return

    const imageFiles: File[] = []
    for (const item of items) {
      if (item.kind === "file" && item.type.startsWith("image/")) {
        const file = item.getAsFile()
        if (file) {
          const ext = file.type === "image/jpeg" ? "jpg" : "png"
          const prefix = isTargetingReply.current ? "risposta-screenshot" : "screenshot"
          const count = (isTargetingReply.current ? pendingReplyFiles.length : pendingFiles.length) + imageFiles.length + 1
          const namedFile = new File([file], `${prefix}-${count}.${ext}`, { type: file.type })
          imageFiles.push(namedFile)
        }
      }
    }

    if (imageFiles.length > 0) {
      if (isTargetingReply.current && isAdmin) {
        setPendingReplyFiles((prev) => [...prev, ...imageFiles])
        notifySuccess(`${imageFiles.length} screenshot incollato nella risposta`)
      } else if (canEditContent) {
        setPendingFiles((prev) => [...prev, ...imageFiles])
        notifySuccess(`${imageFiles.length} screenshot incollato negli allegati`)
      }
    }
  }

  async function handleSave() {
    if (canEditContent && !title.trim()) {
      notifyError(new Error("Il titolo è obbligatorio"))
      return
    }
    setSaving(true)
    try {
      if (isNew) {
        const id = await createBugReport({
          kind,
          title: title.trim(),
          description: description.trim(),
          area: area.trim(),
          severity,
          context: context.trim() || null,
        })
        for (const file of pendingFiles) {
          await uploadBugAttachment(id, file, false)
        }
        notifySuccess("Segnalazione inviata")
      } else {
        if (canEditContent) {
          await updateBugReport(bug.id, {
            kind,
            title: title.trim(),
            description: description.trim(),
            area: area.trim(),
            severity,
            rowVersion: bug.rowVersion,
          })
        }
        if (isAdmin && (status !== bug.status || adminNote !== bug.adminNote)) {
          await updateBugReportStatus(bug.id, {
            status,
            adminNote: adminNote.trim(),
            rowVersion: null,
          })
        }
        for (const file of pendingFiles) {
          await uploadBugAttachment(bug.id, file, false)
        }
        for (const file of pendingReplyFiles) {
          await uploadBugAttachment(bug.id, file, true)
        }
        notifySuccess("Segnalazione aggiornata")
      }
      onSaved()
      onClose()
    } catch (err) {
      notifyError(err)
    } finally {
      setSaving(false)
    }
  }

  async function removeAttachment(attachmentId: number, fileName: string) {
    const ok = await confirm({
      title: "Eliminare l'allegato?",
      description: `«${fileName}» verrà rimosso dalla segnalazione.`,
      confirmLabel: "Elimina",
    })
    if (!ok) return
    try {
      await deleteBugAttachment(attachmentId)
      onSaved()
    } catch (err) {
      notifyError(err)
    }
  }

  // L6: Copia per l'analisi (Markdown blocco BUG-NNN) — formato in bug-report-utils.
  async function handleCopyForAnalysis() {
    if (!bug) return
    await copiaTesto(buildBugMarkdown(bug), `Blocco BUG-${String(bug.id).padStart(3, "0")}`)
  }

  const readOnlyContent = !canEditContent

  const authorAttachments = React.useMemo(
    () => bug?.attachments.filter((a) => !a.isReply) ?? [],
    [bug?.attachments]
  )

  const replyAttachments = React.useMemo(
    () => bug?.attachments.filter((a) => a.isReply) ?? [],
    [bug?.attachments]
  )

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent
        className="flex max-h-[90vh] max-w-2xl flex-col p-6"
        onPaste={handlePaste}
      >
        <DialogHeader>
          <div className="flex items-start justify-between gap-2">
            <div>
              <DialogTitle className="flex items-center gap-2">
                {isNew ? "Nuova segnalazione" : `Segnalazione #${bug.id}`}
                {bug?.fixedInBuild ? (
                  <Badge variant="secondary" className="font-mono text-xs">
                    Risolto in: {bug.fixedInBuild}
                  </Badge>
                ) : null}
              </DialogTitle>
              <DialogDescription>
                {isNew
                  ? "Descrivi il problema: incolla screenshot (Ctrl+V) per mostrare l'errore."
                  : `Aperta da ${bug.createdByName} il ${formatDateTimeShort(bug.createdAt)}${
                      readOnlyContent ? " · sola lettura" : ""
                    }`}
              </DialogDescription>
            </div>
            {!isNew && (isAdmin || canWrite) ? (
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-8 gap-1.5 text-xs shrink-0"
                onClick={() => void handleCopyForAnalysis()}
                title="Copia formato BUG-NNN per analisi e prompt"
              >
                <Copy className="size-3.5" />
                Copia per analisi
              </Button>
            ) : null}
          </div>
        </DialogHeader>

        <div className="min-h-0 flex-1 space-y-4 overflow-y-auto py-1 pr-1">
          {/* Griglia Tipo e Gravità */}
          <div className="grid grid-cols-2 gap-4">
            <div className="grid min-w-0 gap-2">
              <Label>Tipo</Label>
              <Select
                value={kind}
                onValueChange={(v) => setKind(v as BugKind)}
                disabled={readOnlyContent}
              >
                <SelectTrigger className="w-full min-w-0">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="BUG">Bug</SelectItem>
                  <SelectItem value="IMPROVEMENT">Miglioria</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="grid min-w-0 gap-2">
              <Label>Gravità</Label>
              <Select
                value={severity}
                onValueChange={(v) => setSeverity(v as BugSeverity)}
                disabled={readOnlyContent}
              >
                <SelectTrigger className="w-full min-w-0">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="HIGH">Alta</SelectItem>
                  <SelectItem value="MEDIUM">Media</SelectItem>
                  <SelectItem value="LOW">Bassa</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="grid gap-2">
            <Label>Titolo</Label>
            {/* onFocus: rimette il mirino dell'incolla sugli allegati della segnalazione.
                Senza, dopo aver toccato la risposta ogni Ctrl+V successivo finirebbe lì. */}
            <Input
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              onFocus={() => {
                isTargetingReply.current = false
              }}
              placeholder="Cosa succede, in una riga"
              disabled={readOnlyContent}
            />
          </div>

          <div className="grid gap-2">
            <Label>Dove succede</Label>
            <Input
              value={area}
              onChange={(e) => setArea(e.target.value)}
              onFocus={() => {
                isTargetingReply.current = false
              }}
              placeholder="es. Preventivi, Commesse, Gestore DDP, Bilancio…"
              disabled={readOnlyContent}
            />
          </div>

          <div className="grid gap-2">
            <Label>Descrizione</Label>
            <Textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              onFocus={() => {
                isTargetingReply.current = false
              }}
              rows={5}
              placeholder="Cosa hai fatto, cosa ti aspettavi e cosa è successo invece."
              disabled={readOnlyContent}
              className="field-sizing-content min-h-24"
            />
          </div>

          {/* L2: Contesto Tecnico Automatico */}
          {context ? (
            <div className="rounded-md border bg-muted/20 p-2.5 text-xs">
              <div className="flex items-center justify-between">
                <span className="flex items-center gap-1.5 font-medium text-muted-foreground">
                  <Info className="size-3.5" />
                  Dettagli tecnici catturati
                </span>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="h-6 px-2 text-xs"
                  onClick={() => setContextOpen((prev) => !prev)}
                >
                  {contextOpen ? (
                    <>
                      Nascondi <ChevronUp className="ml-1 size-3" />
                    </>
                  ) : (
                    <>
                      Mostra <ChevronDown className="ml-1 size-3" />
                    </>
                  )}
                </Button>
              </div>
              <Collapsible open={contextOpen} className="mt-2">
                <pre className="max-h-36 overflow-y-auto whitespace-pre-wrap rounded bg-muted/60 p-2 font-mono text-[11px] text-muted-foreground leading-relaxed">
                  {context}
                </pre>
              </Collapsible>
            </div>
          ) : null}

          {/* ── Allegati Segnalazione ── */}
          <div className="grid gap-2">
            <div className="flex items-center justify-between">
              <Label>Allegati (screenshot o file)</Label>
              {canEditContent ? (
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => {
                    isTargetingReply.current = false
                    fileRef.current?.click()
                  }}
                >
                  <Paperclip className="size-3.5" />
                  Aggiungi
                </Button>
              ) : null}
              <input
                ref={fileRef}
                type="file"
                multiple
                accept="image/*,application/pdf"
                className="hidden"
                onChange={(event) => {
                  const files = Array.from(event.target.files ?? [])
                  if (files.length > 0) setPendingFiles((prev) => [...prev, ...files])
                  event.target.value = ""
                }}
              />
            </div>

            {authorAttachments.length > 0 ? (
              <div className="flex flex-wrap gap-2">
                {authorAttachments.map((attachment) => (
                  <BugAttachmentThumb
                    key={attachment.id}
                    attachment={attachment}
                    canDelete={canDeleteAttachment(attachment)}
                    onDelete={() =>
                      void removeAttachment(attachment.id, attachment.fileName)
                    }
                  />
                ))}
              </div>
            ) : null}

            {pendingFiles.length > 0 ? (
              <div className="flex flex-wrap gap-1.5">
                {pendingFiles.map((file, idx) => (
                  <Badge
                    key={`${file.name}-${idx}`}
                    variant="outline"
                    className="max-w-full gap-1 font-normal"
                  >
                    <span className="min-w-0 truncate" title={file.name}>
                      {file.name}
                    </span>
                    <span className="shrink-0 text-muted-foreground">
                      {formatSize(file.size)}
                    </span>
                    <button
                      type="button"
                      onClick={() =>
                        setPendingFiles((prev) => prev.filter((_, i) => i !== idx))
                      }
                      title="Togli dalla lista"
                    >
                      <X className="size-3" />
                    </button>
                  </Badge>
                ))}
              </div>
            ) : null}

            {!authorAttachments.length && pendingFiles.length === 0 ? (
              <p className="text-xs text-muted-foreground">
                Trascina o incolla (Ctrl+V) uno screenshot: vale più di mezza pagina di descrizione.
              </p>
            ) : null}
          </div>

          {/* ── Gestione e Risposta (L9) ── */}
          {!isNew ? (
            <div className="grid gap-3 rounded-md border bg-muted/30 p-3.5">
              <div className="flex items-center gap-2">
                <Label className="mb-0">Stato</Label>
                {!isAdmin ? (
                  <Badge variant="outline" className={cn(STATUS_META[bug.status].className)}>
                    {STATUS_META[bug.status].label}
                  </Badge>
                ) : null}
              </div>

              {isAdmin ? (
                <Select value={status} onValueChange={(v) => setStatus(v as BugStatus)}>
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {STATUS_ORDER.map((s) => (
                      <SelectItem key={s} value={s}>
                        {STATUS_META[s].label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              ) : null}

              <div className="grid gap-1.5">
                <Label>Risposta</Label>
                {isAdmin ? (
                  <Textarea
                    value={adminNote}
                    onChange={(e) => setAdminNote(e.target.value)}
                    onFocus={() => {
                      isTargetingReply.current = true
                    }}
                    rows={3}
                    placeholder="Cosa è stato fatto, o perché non si fa."
                    className="field-sizing-content min-h-16 bg-background"
                  />
                ) : (
                  <p className="whitespace-pre-wrap text-sm text-muted-foreground">
                    {bug.adminNote || "— nessuna risposta ancora —"}
                  </p>
                )}
              </div>

              {/* L9: Allegati alla risposta */}
              <div className="grid gap-2 border-t pt-2">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-medium text-muted-foreground">
                    Foto allegate alla risposta
                  </span>
                  {isAdmin ? (
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      className="h-7 text-xs"
                      onClick={() => {
                        isTargetingReply.current = true
                        replyFileRef.current?.click()
                      }}
                    >
                      <Paperclip className="mr-1 size-3" />
                      Allega foto risposta
                    </Button>
                  ) : null}
                  <input
                    ref={replyFileRef}
                    type="file"
                    multiple
                    accept="image/*,application/pdf"
                    className="hidden"
                    onChange={(event) => {
                      const files = Array.from(event.target.files ?? [])
                      if (files.length > 0) {
                        setPendingReplyFiles((prev) => [...prev, ...files])
                      }
                      event.target.value = ""
                    }}
                  />
                </div>

                {replyAttachments.length > 0 ? (
                  <div className="flex flex-wrap gap-2">
                    {replyAttachments.map((attachment) => (
                      <BugAttachmentThumb
                        key={attachment.id}
                        attachment={attachment}
                        canDelete={canDeleteAttachment(attachment)}
                        onDelete={() =>
                          void removeAttachment(attachment.id, attachment.fileName)
                        }
                      />
                    ))}
                  </div>
                ) : null}

                {pendingReplyFiles.length > 0 ? (
                  <div className="flex flex-wrap gap-1.5">
                    {pendingReplyFiles.map((file, idx) => (
                      <Badge
                        key={`${file.name}-${idx}`}
                        variant="secondary"
                        className="max-w-full gap-1 font-normal"
                      >
                        <span className="min-w-0 truncate" title={file.name}>
                          [Risposta] {file.name}
                        </span>
                        <span className="shrink-0 text-muted-foreground">
                          {formatSize(file.size)}
                        </span>
                        <button
                          type="button"
                          onClick={() =>
                            setPendingReplyFiles((prev) => prev.filter((_, i) => i !== idx))
                          }
                          title="Togli dalla lista"
                        >
                          <X className="size-3" />
                        </button>
                      </Badge>
                    ))}
                  </div>
                ) : null}

                {!replyAttachments.length && pendingReplyFiles.length === 0 ? (
                  <p className="text-[11px] text-muted-foreground">
                    Nessuna foto allegata alla risposta.
                  </p>
                ) : null}
              </div>
            </div>
          ) : null}
        </div>

        <DialogFooter className="border-t pt-4">
          <Button variant="outline" onClick={onClose} disabled={saving}>
            {readOnlyContent && !isAdmin ? "Chiudi" : "Annulla"}
          </Button>
          {canEditContent || isAdmin ? (
            <Button onClick={() => void handleSave()} disabled={saving}>
              {saving ? <Loader2 className="mr-2 size-4 animate-spin" /> : null}
              {isNew ? "Invia segnalazione" : "Salva"}
            </Button>
          ) : null}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
