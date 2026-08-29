import { useMutation, useQuery } from "@tanstack/react-query"

import { fetchHrDayReminder, sendHrDayReminder } from "@/lib/api/hr"
import { formatDateShort, formatDateTimeShort } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"

import { AnteprimaMailDialog, type MessaggioMail } from "./AnteprimaMailDialog"

interface SollecitoGiornataDialogProps {
  /** Giornata da segnalare; null = dialogo chiuso. */
  target: { employeeId: number; date: string } | null
  onOpenChange: (open: boolean) => void
  /** Chiamato dopo un invio riuscito: il cartellino si ricarica (il 📧 diventa opaco). */
  onSent: () => void
}

/**
 * Il sollecito della singola giornata (PIANO-HR-PORT-ORIGINALE.md, voce 1): port di
 * `btnMailDipendente_Click`.
 *
 * <p>Il testo lo compone il server e qui si legge per intero prima di spedire — è
 * l'anteprima della voce 4, lo stesso dialogo del sollecito mensile.</p>
 *
 * <p>Due strade come nel resto del modulo: se l'SMTP è configurato la mail parte dal
 * server, altrimenti si apre il client di posta e al server si dice soltanto che gliel'
 * abbiamo messa davanti.</p>
 */
export function SollecitoGiornataDialog({
  target,
  onOpenChange,
  onSent,
}: SollecitoGiornataDialogProps) {
  const sollecitoQuery = useQuery({
    queryKey: ["hr-day-reminder", target?.employeeId, target?.date],
    queryFn: () => fetchHrDayReminder(target!.employeeId, target!.date),
    enabled: target != null,
  })

  const sollecito = sollecitoQuery.data
  const viaSmtp = sollecito?.smtpEnabled === true

  const invia = useMutation({
    mutationFn: async () => {
      if (!sollecito || !target) return
      if (!viaSmtp) {
        // Il client di posta: una finestra sola, quindi nessuno sfalsamento come nel
        // sollecito di gruppo.
        const url =
          `mailto:${sollecito.email}` +
          `?subject=${encodeURIComponent(sollecito.subject)}` +
          `&body=${encodeURIComponent(sollecito.body)}`
        window.open(url, "_self")
      }
      await sendHrDayReminder(target.employeeId, target.date, viaSmtp ? "SMTP" : "MAILTO")
    },
    onSuccess: () => {
      notifySuccess(
        viaSmtp
          ? `Sollecito inviato a ${sollecito?.email}.`
          : "Sollecito aperto nel client di posta e registrato."
      )
      onSent()
      onOpenChange(false)
    },
    onError: (e) => notifyError(e instanceof Error ? e.message : "Sollecito non riuscito."),
  })

  // Finché il testo non è arrivato — o se la giornata non si sollecita — il dialogo mostra
  // un solo riquadro col motivo, invece di una mail vuota.
  const messaggi: MessaggioMail[] | null =
    target == null
      ? null
      : sollecito != null && sollecito.blocco === ""
        ? [
            {
              id: sollecito.employeeId,
              nome: sollecito.employeeName,
              email: sollecito.email,
              subject: sollecito.subject,
              body: sollecito.body,
            },
          ]
        : [
            {
              id: target.employeeId,
              nome: sollecito?.employeeName ?? "",
              email: sollecito?.email,
              subject: sollecito?.subject ?? "",
              body: sollecitoQuery.isLoading
                ? "Caricamento…"
                : sollecitoQuery.error
                  ? (sollecitoQuery.error as Error).message
                  : sollecito?.blocco || "Giornata non disponibile.",
            },
          ]

  const gia = sollecito?.lastReminderAt
  const descrizione = [
    target ? `Giornata del ${formatDateShort(target.date)}.` : "",
    gia ? `Sollecito già inviato il ${formatDateTimeShort(gia)}.` : "",
    sollecito && !viaSmtp && sollecito.blocco === ""
      ? "SMTP non configurato: si apre il client di posta."
      : "",
  ]
    .filter(Boolean)
    .join(" ")

  return (
    <AnteprimaMailDialog
      messaggi={messaggi}
      titolo="Segnalazione timbrature"
      descrizione={descrizione}
      confermaLabel={viaSmtp ? "Invia email" : "Apri nel client di posta"}
      inviando={invia.isPending}
      onConferma={() => {
        if (!sollecito || sollecito.blocco !== "") {
          onOpenChange(false)
          return
        }
        invia.mutate()
      }}
      onOpenChange={onOpenChange}
    />
  )
}
