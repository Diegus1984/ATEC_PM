# Regola — Coppie di date "Inizio / Fine"

Regola UX riusabile per qualsiasi coppia di date **inizio → fine** in un form
(es. attività: *Data check* → *Data close*; fase: *inizio* → *fine*; periodo, ecc.).
Pensata per essere portata su **qualsiasi software/stack** (la logica è
indipendente dal framework; in fondo c'è l'implementazione React/TS di riferimento).

---

## 1. La regola (3 comportamenti)

Date due date in coppia, **Inizio** e **Fine**:

1. **Fine ≥ Inizio** — nel selettore della *Fine* sono **disabilitati** tutti i
   giorni **precedenti** all'*Inizio*. Non si può scegliere una fine antecedente
   all'inizio. (Vincolo *inclusivo*: Fine = Inizio è ammesso.)

2. **Allinea la Fine all'Inizio** — quando si cambia l'*Inizio*:
   - se la *Fine* è **vuota** → la *Fine* prende il valore dell'*Inizio*;
   - se la *Fine* è **anteriore** al nuovo *Inizio* → la *Fine* viene **riportata**
     al nuovo *Inizio*;
   - se la *Fine* è già valida (≥ nuovo Inizio) → resta invariata.

3. **Fine inibita senza Inizio** — finché l'*Inizio* è vuoto, il campo *Fine* è
   **disabilitato**. Se si **svuota** l'*Inizio*, la *Fine* viene **azzerata**
   (e torna disabilitata).

### Perché
- Impedisce a monte stati impossibili (fine < inizio) invece di validarli dopo.
- L'auto-allineamento riduce i click: nel caso comune (attività di un giorno)
  basta impostare l'inizio.
- L'inibizione della fine senza inizio rende l'ordine di compilazione ovvio.

---

## 2. Logica pura (pseudo-codice, indipendente dal linguaggio)

Si lavora con date **"solo giorno"** (senza ora/fuso). Il confronto è tra date di
calendario, non tra timestamp.

```
# Stato: inizio, fine  (ciascuno = data oppure VUOTO)

onChangeInizio(nuovoInizio):
    inizio = nuovoInizio
    if nuovoInizio è VUOTO:
        fine = VUOTO                      # regola 3
    else if fine è VUOTO oppure fine < nuovoInizio:
        fine = nuovoInizio                # regola 2

onChangeFine(nuovaFine):
    fine = nuovaFine                      # nessun ricalcolo dell'inizio

# Vincoli da imporre alla UI del campo FINE:
campoFine.disabilitato      = (inizio è VUOTO)        # regola 3
campoFine.giorniDisabilitati = giorni < inizio        # regola 1 (inclusiva)
```

> Nota confronto: con date in formato ISO `YYYY-MM-DD` il confronto `<` tra
> stringhe equivale al confronto cronologico (ordinamento lessicografico = ordine
> di calendario). In altri linguaggi confronta oggetti `Date`/`DateOnly`
> normalizzati a mezzanotte locale.

---

## 3. Insidie (validità cross-stack)

- **Fuso orario**: usare date *solo giorno*. Costruire la `Date` da
  anno/mese/giorno **locali**, mai `new Date("2026-06-21")` (che è interpretata
  UTC e può slittare di un giorno). In C# usare `DateOnly`.
- **Confronto inclusivo**: Fine = Inizio deve essere **permesso** (è il caso
  "attività di un giorno"). Disabilitare solo i giorni *strettamente* precedenti.
- **Svuotamento**: gestire esplicitamente il caso "Inizio svuotato" → azzera Fine.
- **Validazione lato server**: la regola UI è comodità; il server deve comunque
  rifiutare `fine < inizio` come ultima difesa.

---

## 4. Implementazione di riferimento (React + TypeScript)

Stack origine: React 19 + shadcn/ui (`Popover` + `Calendar` di react-day-picker).

### 4.1 Helper date "solo giorno" (anti-fuso)

```ts
// date-iso.ts — conversione ISO (yyyy-MM-dd) ⇄ Date locale, senza slittamenti di fuso.

export function isoToDate(value: string | null | undefined): Date | undefined {
  if (!value) return undefined
  const [year, month, day] = value.slice(0, 10).split("-").map(Number)
  if (!year || !month || !day) return undefined
  return new Date(year, month - 1, day) // costruzione LOCALE: niente slittamento
}

export function dateToIso(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, "0")
  const day = String(date.getDate()).padStart(2, "0")
  return `${year}-${month}-${day}`
}
```

### 4.2 Campo data con vincoli (`disableBefore` / `disabled`)

Il selettore della Fine riceve due prop chiave:
- `disabled` → regola 3 (inibito senza inizio);
- `disableBefore` → regola 1 (giorni prima dell'inizio disabilitati).

```tsx
interface DateFieldProps {
  value: string | null
  onChange: (value: string | null) => void
  /** Disabilita i giorni precedenti a questa data (es. fine ≥ inizio). */
  disableBefore?: Date
  /** Disabilita del tutto il campo (es. inibire la fine finché manca l'inizio). */
  disabled?: boolean
  // ...placeholder, size, clearable
}

// Dentro il calendario (react-day-picker):
const disabledDays = [
  ...(disableBefore ? [{ before: disableBefore }] : []), // { before } è esclusivo → Fine = Inizio resta ammesso
]
// <Calendar disabled={disabledDays} ... />
```

### 4.3 Aggancio nel form (la regola vera e propria)

```tsx
<div className="grid grid-cols-2 gap-3">
  {/* INIZIO */}
  <DateField
    value={draft.dataCheck}
    onChange={(value) => {
      // Inizio: allinea la fine se mancante o anteriore; se si svuota
      // l'inizio si azzera anche la fine (resta inibita finché manca l'inizio).
      const next = { ...draft, dataCheck: value }
      if (!value) {
        next.dataClose = null
      } else {
        const end = draft.dataClose?.slice(0, 10) ?? null
        if (!end || end < value) next.dataClose = value
      }
      onChange(next)
    }}
  />

  {/* FINE */}
  <DateField
    value={draft.dataClose}
    onChange={(value) => onChange({ ...draft, dataClose: value })}
    disabled={!draft.dataCheck}                       // regola 3
    disableBefore={isoToDate(draft.dataCheck)}        // regola 1
  />
</div>
```

---

## 5. Adattamento ad altri stack (cheat-sheet)

| Concetto | React/TS (origine) | C# / WPF | Altro |
|---|---|---|---|
| Data solo-giorno | stringa ISO `yyyy-MM-dd` + helper | `DateOnly` | tipo date-only nativo |
| "Fine disabilitata senza inizio" | prop `disabled={!inizio}` | `IsEnabled="{Binding HasInizio}"` | binding/stato |
| "Giorni prima dell'inizio disabilitati" | `disableBefore` → matcher calendario | `DatePicker.DisplayDateStart = Inizio` o `BlackoutDates` | min-date del picker |
| Allinea fine all'inizio | logica in `onChange` dell'inizio | setter di `Inizio` nel ViewModel | handler change |
| Confronto inclusivo (Fine = Inizio ok) | `{ before }` esclusivo | `< Inizio` strettamente | `<` non `<=` |

### Logica equivalente in C# (ViewModel WPF)

```csharp
public DateOnly? Inizio
{
    get => _inizio;
    set
    {
        _inizio = value;
        if (value is null)
            Fine = null;                              // regola 3
        else if (Fine is null || Fine < value)
            Fine = value;                             // regola 2
        OnPropertyChanged(nameof(Inizio));
        OnPropertyChanged(nameof(HasInizio));
    }
}

public DateOnly? Fine { get; set; }                   // nessun ricalcolo dell'inizio
public bool HasInizio => Inizio is not null;          // regola 3 → lega IsEnabled del campo Fine
// Campo Fine: DisplayDateStart = Inizio (regola 1)   → blocca i giorni precedenti
```

---

*Origine: ATEC PM — client web `atec-pm-web`. Applicata alle attività del modulo
MoM (Data check → Data close).*
