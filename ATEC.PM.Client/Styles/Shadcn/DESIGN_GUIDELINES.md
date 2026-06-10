# Linee Guida di Design - ATEC PM (Shadcn/UI per WPF)

Questa guida definisce le regole e le best practice per progettare e sviluppare tutte le nuove pagine, finestre e controlli all'interno dell'applicazione ATEC PM, garantendo coerenza estetica e funzionale con il design system ispirato a **Shadcn/UI**.

---

## 1. Fondazioni del Design System

### 1.1 Tipografia e Font
Il font ufficiale del progetto è **Inter**. È incorporato come risorsa dell'applicazione ed è disponibile globalmente tramite la chiave `{StaticResource ShadcnFontFamily}`.
- **Regola d'oro**: Imposta sempre `FontFamily="{DynamicResource ShadcnFontFamily}"` sul tag radice della tua finestra (`Window` o `UserControl`) in modo che si propaghi automaticamente a tutti gli elementi di testo interni.
- Utilizza le classi tipografiche standard definite in `ShadcnTypography.xaml` per blocchi di testo speciali:
  - Intestazioni principali: `ShadcnH1` (30px, ExtraBold), `ShadcnH2` (24px, SemiBold), `ShadcnH3` (20px, SemiBold).
  - Testo paragrafo standard: `ShadcnP` (14px, Regular).
  - Testo secondario o note: `ShadcnMuted` (13px, Muted Foreground).
  - Testo piccolo: `ShadcnSmall` (12px, Medium).

### 1.2 Gestione dei Colori e dei Pennelli (Brushes)
Tutti i colori derivano dalla tavolozza Zinc/Tailwind.
- **CRITICO**: Fai **sempre** riferimento ai pennelli utilizzando `{DynamicResource NomePennello}` anziché `{StaticResource ...}`. Questo evita crash all'avvio dovuti all'ordine di caricamento dei file BAML in WPF.
- Pennelli principali da utilizzare:
  - Sfondo finestra: `{DynamicResource ShadcnBackgroundBrush}` o `{DynamicResource ShadcnMutedBrush}` (grigio chiaro per dialoghi/login).
  - Sfondo card: `{DynamicResource ShadcnCardBrush}` (bianco puro).
  - Bordo elementi: `{DynamicResource ShadcnBorderBrush}` (grigio neutro chiaro `#E4E4E7`).
  - Testo principale: `{DynamicResource ShadcnForegroundBrush}`.
  - Testo disattivato/secondario: `{DynamicResource ShadcnMutedForegroundBrush}`.

---

## 2. Struttura del Layout

### 2.1 Card e Contenitori
I contenuti delle pagine devono essere organizzati in "Card" visive per dare profondità e pulizia:
- Utilizza un `Border` con lo stile `{DynamicResource ShadcnCard}` (che applica automaticamente sfondo bianco, bordo sottile, angoli arrotondati a `8` e un'ombra sfumata molto leggera).
- Esempio di struttura:
```xml
<Border Style="{StaticResource ShadcnCard}" Margin="16">
    <Grid>
        <!-- Contenuto della pagina qui -->
    </Grid>
</Border>
```

### 2.2 Allineamenti e Spaziature (Padding & Margins)
- Mantieni un padding interno di `24` o `32` per i contenitori principali (Card/Finestre).
- I margini tra i controlli di input verticali dovrebbero essere coerenti (es. `12` o `16` pixel).

---

## 3. Controlli Standard & Stili Applicabili

### 3.1 Input di Testo e Password
- **TextBox**: Utilizza lo stile `{StaticResource ShadcnTextBox}`. Applica angoli arrotondati a `6`, un'altezza standard di `36px` e un anello di focus scuro e marcato.
- **PasswordBox**: Lo stile implicito è già attivo per il progetto. Qualsiasi tag `<PasswordBox />` otterrà lo stile Shadcn (arrotondato, altezza 36px, anello di focus) senza dover specificare alcuno stile esplicito.

### 3.2 Pulsanti (Buttons)
Utilizza le varianti corrette a seconda del peso semantico dell'azione:
1. **Azione Principale (Primary)**: `Style="{StaticResource ShadcnButtonDefault}"` (sfondo scuro Zinc 900, testo bianco).
2. **Azione Secondaria (Outline)**: `Style="{StaticResource ShadcnButtonOutline}"` (sfondo bianco, bordo grigio, testo scuro).
3. **Azione Invisibile (Ghost)**: `Style="{StaticResource ShadcnButtonGhost}"` (senza sfondo né bordo, evidenziato solo al passaggio del mouse).
4. **Azione Pericolosa (Destructive)**: `Style="{StaticResource ShadcnButtonDestructive}"` (sfondo rosso, testo bianco, per eliminazioni o azioni irreversibili).

### 3.3 Tabelle e Dati (DataGrid)
- Applica sempre `Style="{StaticResource ShadcnDataGrid}"` a tutte le tabelle.
- Rende le righe con un'altezza minima generosa (`MinHeight="44"`), rimuove le linee di griglia invasive, aggiunge un feedback di selezione grigio chiaro, e formatta gli header con font semi-bold e scuro.

### 3.4 Menu a Tendina (Expander / Accordion)
Gli expander sono configurati per avere transizioni estremamente fluide (effetto "lazy" tipico del web):
- Gli expander standard ereditano lo stile implicitamente. Hanno una transizione di layout con curva `QuinticEase` (durata `0.5s` per l'apertura e `0.4s` per la chiusura).
- La freccia (chevron) ruota in modo morbido e sincrono con l'espansione.
- Se si inseriscono expander per la navigazione laterale, utilizzare lo stile esplicito `Style="{StaticResource NavExpander}"`.

---

## 4. Finestre di Dialogo e Messaggi Modali

### 4.1 Messaggi di Conferma e Avviso
- **NON utilizzare mai** il classico `MessageBox.Show()` nativo di Windows. Interrompe l'estetica moderna del programma.
- **Utilizza sempre** la classe helper `ShadcnMessageBox`:
```csharp
MessageBoxResult result = ShadcnMessageBox.Show(
    "Vuoi salvare le modifiche?", 
    "Salvataggio", 
    MessageBoxButton.YesNo, 
    MessageBoxImage.Question
);
```
- Questo genererà automaticamente una modale centrata sulla finestra attiva, con angoli arrotondati, pulsanti flat coordinati e icone vettoriali piatte colorate a seconda della gravità dell'avviso.

### 4.2 Finestre di Dialogo Personalizzate (Form)
Quando crei una nuova finestra di dialogo (come inserimento dati):
- Rendi la finestra modale impostando `Owner` sulla finestra principale e chiamando `ShowDialog()`.
- Imposta `WindowStartupLocation="CenterOwner"`.
- Utilizza sfondi neutri e puliti, preferendo card bianche su sfondo leggermente grigio (`ShadcnMutedBrush`).

---

## 5. Esempio Pratico di XAML per Nuova Pagina
Ecco un modello di partenza per un `UserControl` o una nuova `Page`:

```xml
<UserControl x:Class="ATEC.PM.Client.Views.Templates.NewFeatureControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             FontFamily="{DynamicResource ShadcnFontFamily}"
             Background="{DynamicResource ShadcnBackgroundBrush}">
    
    <Grid Margin="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- Titolo Pagina -->
        <StackPanel Grid.Row="0" Margin="0,0,0,24">
            <TextBlock Text="Nuova Funzionalità" Style="{StaticResource ShadcnH2}" />
            <TextBlock Text="Configura i parametri ed esegui le operazioni." Style="{StaticResource ShadcnMuted}" Margin="0,4,0,0" />
        </StackPanel>

        <!-- Card Contenuto -->
        <Border Grid.Row="1" Style="{StaticResource ShadcnCard}">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="*" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>

                <!-- Form Inputs -->
                <StackPanel Grid.Row="0" MaxWidth="400" HorizontalAlignment="Left">
                    <TextBlock Text="NOME ELEMENTO" Style="{StaticResource ShadcnSmall}" Foreground="{DynamicResource ShadcnMutedForegroundBrush}" Margin="0,0,0,6" />
                    <TextBox Style="{StaticResource ShadcnTextBox}" Margin="0,0,0,16" />

                    <TextBlock Text="CATEGORIA" Style="{StaticResource ShadcnSmall}" Foreground="{DynamicResource ShadcnMutedForegroundBrush}" Margin="0,0,0,6" />
                    <ComboBox Style="{StaticResource ShadcnComboBox}" Margin="0,0,0,16">
                        <ComboBoxItem Content="Categoria A" IsSelected="True" />
                        <ComboBoxItem Content="Categoria B" />
                    </ComboBox>
                </StackPanel>

                <!-- Footer Azioni -->
                <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,24,0,0">
                    <Button Content="Annulla" Style="{StaticResource ShadcnButtonOutline}" Width="100" Margin="0,0,8,0" />
                    <Button Content="Salva" Style="{StaticResource ShadcnButtonDefault}" Width="100" />
                </StackPanel>
            </Grid>
        </Border>
    </Grid>
</UserControl>
```
