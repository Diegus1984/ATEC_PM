import * as React from "react"
import { useQueries } from "@tanstack/react-query"

import { fetchHrDayReminder, sendHrDayReminder } from "@/lib/api/hr"
import type { HrDayReminder } from "@/lib/api/types"
import { formatDateShort, formatDateTimeShort } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"

import { AnteprimaMailDialog, type MessaggioMail } from "./AnteprimaMailDialog"

/** Una giornata di una persona da sollecitare. */
export interface SollecitoTarget {
  employeeId: number
  date: string
}

interface SollecitoGiornataDialogProps {
  /** Le giornate da segnalare (una dal cartellino, N dal Controllo di ieri); null = chiuso. */
  targets: SollecitoTarget[] | null
  onOpenChange: (open: boolean) => void
  /** Chiamato dopo un invio riuscito: le pagine si ricaricano (il 📧 diventa opaco). */
  onSent: () => void
}

/**
 * Il sollecito della singola giornata (PIANO-HR-PORT-ORIGINALE.md, voce 1): port di
 * `btnMailDipendente_Click`. Dal 09/09/2026 spedisce anche PIÙ giornate in un colpo (i
 * selezionati del «Controllo di ieri»): una mail per giornata, ognuna col suo testo, tutte
 * in anteprima prima di partire.
 *
 * <p>Il testo lo compone il server e qui si legge per intero prima di spedire — è
 * l'anteprima della voce 4, lo stesso dialogo del sollecito mensile.</p>
 *
 * <p>Due strade come nel resto del modulo: se l'SMTP è configurato le mail partono dal
 * server, altrimenti si apre il client di posta (una finestra per volta) e al server si dice
 * soltanto che gliele abbiamo messe davanti.</p>
 */
export function SollecitoGiornataDialog({
  targets,
  onOpenChange,
  onSent,
}: SollecitoGiornataDialogProps) {
  const [inviando, setInviando] = React.useState(false)
  const lista = React.useMemo(() => targets ?? [], [targets])

  const anteprime = useQueries({
    queries: lista.map((t) => ({
      queryKey: ["hr-day-reminder", t.employeeId, t.date],
      queryFn: () => fetchHrDayReminder(t.employeeId, t.date),
    })),
  })

  // Le giornate che si possono davvero spedire: testo arrivato e nessun blocco.
  const pronte = lista
    .map((t, i) => ({ target: t, sollecito: anteprime[i]?.data as HrDayReminder | undefined }))
    .filter((x): x is { target: SollecitoTarget; sollecito: HrDayReminder } =>
      x.sollecito != null && x.sollecito.blocco === ""
    )
  const viaSmtp = pronte.some((x) => x.sollecito.smtpEnabled)
  const piu = lista.length > 1

  async function invia() {
    if (pronte.length === 0) {
      onOpenChange(false)
      return
    }
    setInviando(true)
    let inviati = 0
    const errori: string[] = []
    try {
      if (!viaSmtp) {
        // Il client di posta, una finestra per volta: aprirle tutte insieme le fa bloccare
        // dal browser. Il dialogo si chiude prima della raffica (come nel calendario).
        onOpenChange(false)
        pronte.forEach(({ sollecito }, indice) => {
          const url =
            `mailto:${sollecito.email}` +
            `?subject=${encodeURIComponent(sollecito.subject)}` +
            `&body=${encodeURIComponent(sollecito.body)}`
          if (indice === 0) window.open(url, "_self")
          else setTimeout(() => window.open(url, "_self"), indice * 900)
        })
      }
      for (const { target, sollecito } of pronte) {
        try {
          await sendHrDayReminder(target.employeeId, target.date, viaSmtp ? "SMTP" : "MAILTO")
          inviati++
        } catch (e) {
          errori.push(
            `${sollecito.employeeName} ${formatDateShort(target.date)}: ${
              e instanceof Error ? e.message : "non riuscito"
            }`
          )
        }
      }
    } finally {
      setInviando(false)
    }

    if (inviati > 0) onSent()
    if (errori.length > 0) {
      notifyError(
        (inviati > 0 ? `Inviati ${inviati}, non riusciti ${errori.length}: ` : "Non riusciti: ") +
          errori.join(" · ")
      )
    } else if (viaSmtp) {
      notifySuccess(
        piu ? `${inviati} solleciti inviati.` : `Sollecito inviato a ${pronte[0].sollecito.email}.`
      )
    } else {
      notifySuccess(
        piu
          ? `${inviati} solleciti aperti nel client di posta e registrati.`
          : "Sollecito aperto nel client di posta e registrato."
      )
    }
    if (viaSmtp) onOpenChange(false)
  }

  // Finché il testo non è arrivato — o se la giornata non si sollecita — al posto della
  // mail si legge il motivo, invece di un corpo vuoto. Con più giornate ognuna è una voce.
  const messaggi: MessaggioMail[] | null =
    targets == null
      ? null
      : lista.map((t, i) => {
          const q = anteprime[i]
          const s = q?.data as HrDayReminder | undefined
          const nome = s?.employeeName ?? ""
          return {
            id: t.employeeId,
            nome: piu ? `${nome || "?"} — ${formatDateShort(t.date)}` : nome,
            email: s?.email,
            subject: s?.subject ?? "",
            body:
              s != null && s.blocco === ""
                ? s.body
                : q?.isLoading
                  ? "Caricamento…"
                  : q?.error
                    ? (q.error as Error).message
                    : s?.blocco || "Giornata non disponibile.",
          }
        })

  const giaInviate = pronte.filter((x) => x.sollecito.lastReminderAt).length
  const bloccate = lista.length - pronte.length
  const descrizione = (
    piu
      ? [
          `${lista.length} giornate selezionate, ${pronte.length} da spedire.`,
          bloccate > 0 ? `${bloccate} non si spediscono (senza email o senza anomalia): vedi le voci.` : "",
          giaInviate > 0 ? `${giaInviate} già sollecitate in passato: si rimanda.` : "",
        ]
      : [
          lista[0] ? `Giornata del ${formatDateShort(lista[0].date)}.` : "",
          pronte[0]?.sollecito.lastReminderAt
            ? `Sollecito già inviato il ${formatDateTimeShort(pronte[0].sollecito.lastReminderAt)}.`
            : "",
        ]
  )
    .concat(pronte.length > 0 && !viaSmtp ? "SMTP non configurato: si apre il client di posta." : "")
    .filter(Boolean)
    .join(" ")

  const confermaLabel = viaSmtp
    ? piu
      ? `Invia ${pronte.length} email`
      : "Invia email"
    : piu
      ? `Apri ${pronte.length} email nel client di posta`
      : "Apri nel client di posta"

  return (
    <AnteprimaMailDialog
      messaggi={messaggi}
      titolo="Segnalazione timbrature"
      descrizione={descrizione}
      confermaLabel={confermaLabel}
      inviando={inviando}
      onConferma={() => void invia()}
      onOpenChange={onOpenChange}
    />
  )
}
