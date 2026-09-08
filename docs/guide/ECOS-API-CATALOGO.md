# EcosAgile API — catalogo del tenant ATEC (API Library)

> Letto il **08/09/2026** da `IT Operation > Api > API Library` del nostro tenant (istanza `dd`,
> ClientID 10305), con l'account di Diego tramite il browser. È l'elenco **configurato** nella
> libreria: **84 API**. Non è tutto: le API «di sistema» che usiamo (`TokenGet`,
> `PeopleBadgeGetAll`, `PeopleStampPost`, `PeoplePost`, `PeopleAbsenceRequestPost`,
> `PeopleOvertimeRequestPost`) **non compaiono qui** ma esistono e rispondono. La tendina
> dell'**API Test Panel** è più larga (comincia da `AccountCRMDataGetAll`, che qui non c'è).
> Manuale d'uso dell'API: [ECOS-API-MANUALE.md](ECOS-API-MANUALE.md).

## Come si legge la tabella

- **Service** = il **ServiceID** che l'utente API deve avere per chiamarla: è la parola che
  compare nell'errore `-2` («ServiceID requested: …»). Quello che manca a `api.it` si chiede
  con questo nome esatto.
- **Criteria** = il **filtro implicito lato server**, applicato SEMPRE in AND ai filtri che
  mandiamo noi. È qui che stanno le sorprese: vedi le trappole sotto.
- **Order by** = l'ordinamento con cui arrivano le pagine (non è garantito stabile: §3.6 del manuale).
- `[%S.EmplID%]`, `[%S.ClientID%]`, `[%S.CompanyCountryID%]` = valori della **sessione**
  dell'utente API (le `*GetESS` si restringono così).

## 🪤 Trappole scoperte leggendo i Criteria (decidono il codice)

1. **`PeopleStampGetAll` torna SOLO le timbrature con `UpdateDate` negli ultimi 60 giorni**
   (`UpdateDate>=DateAdd(dd,-60,dbo.GetDate2())`), qualunque `UpdateDate` mandiamo noi. Quindi:
   - l'**import completo** («Reimporta tutto», che cancella le righe `ECOS` non presenti nello
     scarico) **cancellerebbe da noi tutte le timbrature più vecchie di 60 giorni**, perché Ecos
     non le rimanda più — non perché siano state cancellate là;
   - la **risincronizzazione di un giorno o di un mese più vecchio di 60 giorni** riceve zero
     righe: per «tutti» c'è la rete (non cancella), per la **singola persona** no — toglierebbe
     le sue timbrature di quel giorno;
   - la storia oltre i 60 giorni **vive solo da noi**: l'import va tenuto acceso, non si riparte
     mai «da zero».
   Vedi TODO §11. Esclude anche `TypeCode IN ('BREAK_VIRT','DAY_BREAK')` e i dispositivi che
   non sono `STAMP`.
2. **`PeopleAbsenceRequestGetAll` torna solo le richieste degli ultimi ~90 giorni**
   (`DateBegin>=oggi-90 OR DateEnd>=oggi-60`): le assenze vecchie non tornano più.
3. **`PeopleStampActiveGetESS` filtra `[StatusCode]='A' AND [Delete]=0`**: conferma che sulle
   timbrature esistono **`Delete`** e **`StatusCode`** (`A` = attiva) — e che `PeopleStampGetAll`
   **non** filtra `Delete=0`: le cancellate arrivano con `Delete=1` (TODO §11, punto 🔴).
4. `PeopleStampPeriodDayGetAll` filtra per **`StampDate`** (ultimi 90 giorni), non per
   `UpdateDate`: è la candidata per una fotografia «per giorno» senza il problema del punto 1.
5. `PeopleEmploymentGetALL` = le persone **in forza** (o cessate da meno di 3 mesi): è
   l'anagrafica che ci manca (Service `PersonalData`, da misurare).
6. Esistono le anagrafiche che il 27/08 avevamo cercato con nomi sbagliati:
   **`AnagTSCategoryGetAll`** (causali di assenza/straordinario/smart working, Service
   `TimesheetCausal`) e **`AnagDepartmentGetAll`** (reparti, Service `Department`).

## Time (13)

| ApiName | Service | Method | Criteria (filtro implicito) | Order by |
|---|---|---|---|---|
| `AnagTSCategoryGetAll` | TimesheetCausal | Get | `NoSubModality=0 AND (isAbsence=1 OR RecoverHour=1 OR OverTime=1 OR SmartWork=1) AND (CompanyCountryID is Null OR CompanyCountryID=[%S.CompanyCountryID%])` | RowOrder, DescShort |
| `PeopleAbsenceDayGetAll` | AbsenceReport | Get | `TSDate>=DateAdd(YY,-1,GetDate())` | YearTS DESC, MonthTS DESC, TSDate Desc, EmplID |
| `PeopleAbsenceRequestGetAll` ✅ in uso | PeopleAbsenceRequest | Get | `(DateBegin>=DateAdd(dd,-90,dbo.GetDate2()) OR DateEnd>=DateAdd(dd,-60,dbo.GetDate2()))` | UpdateDate desc |
| `PeopleDismissalAbstentionGetAll` | PeopleDismissal | Get | — | UpdateDate Desc |
| `PeopleDismissalGetAll` | PeopleDismissal | Get | — | UpdateDate desc |
| `PeopleOvertimeRequestGetAll` ✅ | PeopleOvertimeApprove | Get | — | OvertimeDate Desc |
| `PeopleSmartWorkRequestGetAll` | SmartWorkRequestHR | Get | `(DateBegin>=DateAdd(dd,-90,dbo.GetDate2()) OR DateEnd>=DateAdd(dd,-60,dbo.GetDate2()))` | UpdateDate desc |
| `PeopleStampActiveGetESS` | PersonStampESS | Get | `EmplID=[%S.EmplID%] and (CausalCode<>'PS_CHK_DISCARD' or CausalCode is Null or Discard=0) and TypeCode NOT IN ('BREAK_VIRT','DAY_BREAK') AND UpdateDate>=DateAdd(dd,-30,dbo.GetDate2()) AND (DeviceUsageCode='STAMP' OR DeviceUsageCode IS NULL) AND [StatusCode]='A' AND [Delete]=0` | StampDateTime desc |
| `PeopleStampDenialGetAll` | PeopleStamp | Get | `UpdateDate>=DateAdd(dd,-60,dbo.GetDate2())` | UpdateDate desc |
| `PeopleStampGetAll` ✅ in uso | PeopleStamp | Get | `TypeCode NOT IN ('BREAK_VIRT','DAY_BREAK') AND UpdateDate>=DateAdd(dd,-60,dbo.GetDate2()) AND (DeviceUsageCode='STAMP' OR DeviceUsageCode IS NULL)` | UpdateDate desc |
| `PeopleStampPeriodDayGetAll` | PersonStamp | Get | `StampDate>=DateAdd(Day,-90,dbo.GetDate2()) AND StampDate<dbo.GetDate2()` | StampDate Desc, EmplID |
| `PeopleStampPeriodGetAll` | PeopleStamp | Get | `UpdateDate>=DateAdd(dd,-60,dbo.GetDate2())` | UpdateDate Desc |
| `PeopleTimePresenceExpectGetAll` | PersonWorkshift | Get | `ExcludeWFCount=0` | TSDate Desc, EmplID |

## People (10)

| ApiName | Service | Method | Criteria | Order by |
|---|---|---|---|---|
| `AnagJobCodeGetAll` | JobCode | Get | — | UpdateDate Desc |
| `OrgChartTreeOutputUserPost` | OrgChartTreeOutputUser | Post/Put | — | — |
| `PeopleCompanyTaskGetAll` | PeopleCompanyTask | Get | — | PeopleCompanyTaskID |
| `PeopleEmploymentGetALL` | PersonalData | Get | `(BaseView.InForce=1 OR (BaseView.TerminationDate > DateAdd(MM,-3,GetDate())) OR BaseView.ClientID=9355) AND BaseView.ExcludeWFCount=0` | EmplID ASC |
| `PeopleExpressGetAll` | PersonExpress | Get | `ExcludeWFCount<>1` | Updatedate Desc |
| `PeopleExpressLightGetAll` ❌ negata a api.it | PeopleExpressLight | Get | `inForce=1 AND ExcludeWFCount<>1` | EmplID Desc |
| `PeopleExpressMidGetAll` | PeopleExpressMid | Get | `ExcludeWFCount<>1` | Updatedate Desc |
| `PeopleJobGetAll` | JobData | Get | — | EffDateSeq ASC |
| `PeopleJobMidGetAll` | JobData | Get | — | EffDate ASC |
| `TicketRestaurantAccruedGetAll` | TicketRestaurant | Get | — | UpdateDate desc |

## Project & Timesheet (26)

| ApiName | Service | Method | Criteria | Order by |
|---|---|---|---|---|
| `ActivityBModelGetAll` | Project | Get | — | UpdateDate DESC |
| `ActivityBModelOtherCostGetAll` | Project | Get | `BaseView.EffSeq=(SELECT MAX(EffSeq) FROM dbo.AN_ActivityBModel AS ACT WITH (NOLOCK) WHERE ACT.ActivityBModelID=BaseView.ActivityBModelID AND ACT.[Delete]=0)` | UpdateDate DESC |
| `ActivityBModelOtherRevenueGetAll` | Project | Get | idem (ultima versione, non cancellata) | UpdateDate DESC |
| `ActivityBModelPost` | Project | Post/Put | — | — |
| `ActivityBModelResourceGetAll` | Project | Get | idem | UpdateDate DESC |
| `ActivityBModelTrancheGetAll` | Project | Get | idem | UpdateDate DESC |
| `ActivityConsumptiveEconomicsMonthGetAll` | Project | Get | — | ActivityID DESC |
| `ActivityPeopleGetAll` | ActivityAllocation | Get | — | UpdateDate Desc |
| `ActivityTaskWBSPeoplePlanGetAll` | WBSProjectMSS | Get | — | UpdateDate Desc |
| `ActivityWBSTaskGetAll` | WBSProject | Get | — | UpdateDate Desc |
| `ActivityWBSTaskPost` | ProjectExpress | Post/Put | — | — |
| `AnagAccountPost` | Account | Post/Put | — | — |
| `AnagActivityGet` | Project | Get | — | UpdateDate desc |
| `AnagActivityOpportunityPost` | Opportunity | Post/Put | — | — |
| `AnagActivityOtherCostGetAll` | Project | Get | `BaseView.EffSeq=(SELECT MAX(EffSeq) FROM dbo.AnagActivity AS ACT WITH (NOLOCK) WHERE ACT.ActivityID=BaseView.ActivityID AND ACT.[Delete]=0)` | EffDate Desc |
| `AnagActivityOtherReturnGetAll` | Project | Get | idem | EffDate Desc |
| `AnagActivityPeoplePost` | ActivityRequestESS | Post/Put | — | — |
| `AnagActivityPost` | ActivityRequestESS | Post/Put | — | — |
| `AnagActivityProjectGet` | Project | Get | — | UpdateDate desc |
| `AnagProductGetAll` | Product | Get | — | ProductID |
| `ProjectEconomicsGetAll` | Economics | Get | — | UpdateDate DESC |
| `Timesheet2GetAll` ❌ negata a api.it | TimesheetAnalysis | Get | — | UpdateDate Desc, TSDate DESC, EmplID, [Type], ActivityID, TSCategoryID, SourceCode |
| `TimesheetActivityTask2GetAll` | TimesheetActivities | Get | `Modality Not In ('RSAB','RDOM')` | TSDate Desc, EmplID, Modality, ActivityID, TSCategoryID, ActivityTaskID, SourceCode |
| `TimesheetActivityTaskGetAll` | TimesheetActivities | Get | — | Anno Desc, Mese Desc, ActivityID, ActivityTaskID, EmplID |
| `TimesheetGetAll` ❌ negata a api.it | TimesheetAnalysis | Get | — | YearI Desc, MonthI Desc, EmplID |
| `TimesheetListGetAll` | TimesheetList | Get | — | YearI DESC, MonthI DESC, EmplID |

## Set-up (4)

| ApiName | Service | Method | Criteria | Order by |
|---|---|---|---|---|
| `AnagCdCGettAll` (sic, doppia t) | CdC | Get | — | UpdateDate Desc |
| `AnagCdCPost` | CdC | Post/Put | — | — |
| `AnagDepartmentGetAll` | Department | Get | — | UpdateDate Desc |
| `MarketAreaGetAll` | MarketAreaESS | Get | — | UpdateDate DESC |

## Payroll (4)

| ApiName | Service | Method | Criteria | Order by |
|---|---|---|---|---|
| `PayrollCostDetailGetAll` | PayrollResult | Get | `(YearI<Year(GetDate()) or (YearI=Year(GetDate()) and MonthI<=Month(GetDate())))` | YearI DESC, MonthI Desc |
| `PayrollResultGetAll` | PayrollResult | Get | — | YearI DESC, MonthI DESC |
| `PayrollResultLastGet` | PayrollResult | Get | `ExcludeWFCount=0` | YearI DESC, MonthI DESC |
| `PayrollResultPost` | PayrollResult | Post/Put | — | — |

## Expense (4)

| ApiName | Service | Method | Criteria | Order by |
|---|---|---|---|---|
| `CreditCardTransactionGetAll` | PeopleCard | Get | — | UpdateDate Desc |
| `PeopleExpenseGetAll` | ExpenseHR | Get | `isToll=0 AND (ExpenseCategoryCode is Null OR ExpenseCategoryCode<>'EXP_IND') AND UpdateDate>=DateAdd(dd,-120,dbo.GetDate2())` | UpdateDate desc |
| `PeopleExpenseReimbursementYearMonthGetAll` | ExpenseHR | Get | — | NameComplete ASC |
| `TransferTypeGetESS` | TransferPlanESS | Get | `EmplID=[%S.EmplID%] AND ESSEnable=1 and IsService=0` | TransferTypeID ASC |

## CRM (5) · Administration/Procurement (2) · Compensation (2) · Learning (1) · Talent (1) · EcosStudio (2)

| ApiName | Service | Module | Method | Criteria | Order by |
|---|---|---|---|---|---|
| `AnagAccountCurrentGetESS` | AccountAdministrationESS | CRM | Get | `StatusCode='A'` | UpdateDate Desc |
| `AnagAccountGetAll` | Account | CRM | Get | — | UpdateDate Desc |
| `AnagAccountReferenceGetAll` | AccountReference | CRM | Get | — | UpdateDate Desc |
| `AnagActivityOpportunityGetAll` | Opportunity | CRM | Get | — | UpdateDate desc |
| `MarketGetAll` | Market | CRM | Get | — | UpdateDate Desc |
| `AnagAccountGetOne` | AccounteCommerce | Administration/Procurement | Get | — | UpdateDate Desc |
| `OrderPurchaseGetAll` | OrderPurchase | Administration/Procurement | Get | — | UpdateDate Desc |
| `PeopleCompensationCurrentGetAll` | CompensationRead | Compensation | Get | — | EmplID ASC, RateCodeID ASC |
| `SalaryPlanRequestGetAll` | SalaryPlan | Compensation | Get | — | SalaryPlanRequestID Desc |
| `PeopleCourseCompanyGetAll` | PeopleCourse | Learning | Get | `CourseType='COMPANY'` | PeopleCourseID DESC |
| `AppraisalPeopleEvaluationGetAll` | AppraisalEvaluation | Talent | Get | `InForce=1` | UpdateDate desc |
| `ProductRequirementGetAll` | EcosAgileReleaseNotes | EcosStudio | Get | — | ProductRequirementID desc |
| `ProductRequirementPost` | EcosAgileReleaseNotes | EcosStudio | Post/Put | — | — |

## Recruiting (10)

| ApiName | Service | Method | Criteria | Order by |
|---|---|---|---|---|
| `AnagVideoInterviewGet` | CVPSS | Get | `VideoInterview=1 AND InterviewStatus Not In ('C','D') AND VideoInterviewDueDate>=GetDate()` | InterviewDate Desc |
| `CVCareerPost` | CV | Post/Put | — | — |
| `CVLimitedGetAll` | CVLimited | Get | `IsEmployee=0` | InsertDate Desc |
| `CVPost` | CV | Post/Put | — | — |
| `RecruitingSettingCompanyGetAll` | CVExternalFormSetting | Get | `ClientID=[%S.ClientID%]` | ClientID |
| `VideoInterviewRunAnswerGetAll` | VideoInterviewHR | Get | — | VideoInterviewRunID DESC, QuestionID ASC |
| `VideoInterviewRunAnswerPostPSS` | CVPSS | Post/Put | — | — |
| `VideoInterviewRunGetAll` | VideoInterviewHR | Get | — | VideoInterviewRunID DESC |
| `VideoInterviewRunPostPSS` | CVPSS | Post/Put | — | — |
| `VideoInterviewTemplateQuestionGetAll` | VideoInterviewTemplateQuestionRead | Get | — | VideoInterviewTemplateID |

## Fuori dalla Library ma esistenti (calibrate il 27/08 e il 08/09/2026)

| ApiName | Esito con `api.it` | ServiceID (dall'errore -2) |
|---|---|---|
| `TokenGet` | ✅ | — |
| `PeopleBadgeGetAll` | ✅ lettura | — |
| `PeopleStampPost` | ✅ scrivibile | — |
| `PeoplePost` | ❌ | JobData (RightID 2) |
| `PeopleAbsenceRequestPost` | ❌ | PeopleAbsenceRequestMSS (RightID 4) |
| `PeopleOvertimeRequestPost` | ❌ | PeopleOvertimeApproveMSS (RightID 4) |
| `PeopleGetAll`, `PeopleAbsenceTypeGetAll`, `PeopleDepartmentGetAll`, `Timesheet2GetALL`, `Timesheet2GetESS` | `-99` | non esistono (i nomi giusti sono nella tabella sopra) |

---

# Dettaglio delle API che ci riguardano (scheda «API_READ» della Library, 08/09/2026)

> Ogni API ha una scheda raggiungibile direttamente:
> `https://ha.ecosagile.com/dd/extranet.pm?ClientID=10305#DetailPage.pm?ComponentID=API_READ&PageID=API_READ&Key0=<ApiName>&MenuVoiceID=1757&ClientID=10305`
> con **Main Data** (servizio richiesto, **livello utente richiesto**, criteria), **Data mapping**
> (tutti i campi, tipo, lunghezza, default) e **Parameters** (= l'ALLOWED LIST dei filtri: fuori da
> qui è `-13`), più la scheda **Enabled users** (chi ha il diritto, con il valore del RightID).
> Funziona anche per le API che la lista non mostra (`PeopleStampPost`, `PeopleAbsenceRequestPost`).

## `PeopleStampGetAll` — timbrature (Service `PeopleStamp`, livello 2)

**Filtri ammessi (15)**: `Abnormal`, `BadgeCode`, `Delete`, `EmplID`, `FraudAlert`, `PayrollCode`,
`ReadLanguage`, `StampActivityID`, `StampDate`, `StampDateTime`, `StampID`, `StampLocationCode`,
`StampLocationID`, `UpdateDate`, `YearMonth`. 🪤 `EmplCode` **non** è filtrabile; `Delete` sì
(`Delete==0` per escludere le cancellate, o leggerlo per propagarle); `StampDate` è la data sola.

**Campi (44)**: `StampID`, `StampDate`, `EmplID`, `EmplCode`, `Delete`, `NameComplete`,
`StampActivityID`, `Regular`, `Single`, `VersusCode`, `Discard`, `BadgeCode`, `Abnormal`, `TypeCode`,
`YearMonth`, `GPSAccuracy`, `CheckDate`, `CausalCode`, `FraudAlert` («campo sospetto: sync
irregolare dell'APP»), `QRCode`, `Note`, `StampActivityDescShort`, `UserTZ`, `StampCausalID`,
`StampCausalCode`, `StampCausalDescShort`, `CF`, `StampDateTime`, `GPSAddress`, `PayrollCode`,
`StatusCode`, `PhoneDateTime`, `CompanyCode`, `PictureName`, `UpdateDate`, `GPSLat`, `GPSLong`,
`LocationID`, `AppStampID`, `InsertDate`, `UniqueDeviceID`, `StampLocationCode`, `StampLocationID`,
`StampLocationName`. Noi ne leggiamo 10.

**Enabled users** (tutti ruolo «Temporaneo», livello 2, diritto **4**): `maria.carretta`,
`api.eclock1`, `api.eclock2`, `api.eclock3` (i **terminali di timbratura** passano da qui),
`api.it` (ultimo accesso 08/09 09:18 = la nostra calibrazione).

## `PeopleStampPost` — scrittura timbrature (Service `PeopleStampPost`, livello 2, Post/Put)

**Campi (40)**: `WriteLanguage`, **`StampID`** (chiave per `Edit=true`), `Delete`, `StampActivityID`,
`StampAccountID`, `StampAction`, `VersusCode`, `BadgeCode`, `IPLat`, `IPLong`, `TypeCode` (default
**`ECLOCK`**), `GPSAccuracy`, `QRCode`, `GreenPass`, `Note`, `PresenceDeviceID`, `UserTZ`,
`StampActivityTaskID`, `StampCausalID` (calcolato dal `PresenceDeviceID`), `RowHash`, `Termoscanner`,
`DayI`, `Fingerprint`, **`StampDateTime`**, `DeviceInfraRedReaderValue`, `GPSAddress`, `StatusCode`
(default **`A`**), `Picture` (File FS), `PhoneDateTime`, `PictureName`, `GPSLat`, `GPSLong`,
`AppStampID`, `UniqueDeviceID`, `AppCounter`, `AppCounterDateTime`, `StampLocationID`, `CommonName`,
`AppUpdateDate`, `AppInsertDate`. Nessun campo marcato obbligatorio nella scheda; la validazione a
vuoto si è fermata su `StampDateTimeTZOffSet` (colonna interna, non in elenco): probabilmente
si soddisfa passando **`UserTZ`** (intero, come nei vecchi sample: `getTimezoneOffset()`).

**Enabled users**: gli stessi cinque di `PeopleStampGetAll`, compresi i tre `api.eclock*`: è
l'API con cui i terminali scrivono le timbrature. `api.it` ✅.

## `PeopleAbsenceRequestGetAll` — richieste di assenza (Service `PeopleAbsenceRequest`, livello 2)

**Filtri ammessi (13)**: `AbsenceRequestID`, `CompanyCode`, `DateBegin`, `DateEnd`, `Duration`,
`EmplID`, `InsertDate`, `LocationID`, `ReadLanguage`, `SourceCode`, `StatusCode`, `UpdateDate`,
`YearMonth`. 📌 **`DateBegin`/`DateEnd`/`YearMonth` sono filtrabili**: il periodo si può chiedere
(dentro la finestra implicita dei ~90 giorni).

**Campi (30)**: `AbsenceRequestID` (chiave interna), `AbsenceRequestCode` (id visibile all'utente),
`DateBegin`, `DateEnd` (opzionale su un giorno), `FullDay` (0/1), `HourBegin`, `HourEnd`,
`HourBeginExpect`, `HourEndExpect`, `YearMonth`, `Duration`, `CategoryID`, `CategoryCode`,
`CategoryDescShort`, `DataApprove`, `ApproveNameComplete`, `EmplID`, `NameComplete`, `CompanyCode`,
`SourceCode` (fonte dell'inserimento), `Note`, `ApproveReply`, **`StatusCode`** = `ACCEPTED`
(accettata) / `REQUEST` (richiesta) / **`REJECT`** (respinta), `ResidualToDate`, `ResidualEndYear`,
`DataSourceCode`, `InsertDate`, `UpdateDate`, `UserTZ`, **`Delete`** (0/1, eliminazione logica).
🪤 Il nostro `SyncAbsences` confronta con `REJECTED` e `CANCELLED`: **`REJECT` non combacia** e una
respinta finisce come «in attesa» (TODO §11).

**Enabled users**: `maria.carretta`, `api.it` (diritto 4).

## `PeopleAbsenceRequestPost` — scrivere una richiesta (Service `PeopleAbsenceRequestMSS`, **livello 3 - Manager**, Post/Put)

**Campi (22)**: `AbsenceRequestID` (chiave), `DateBegin`, `DateEnd`, `FullDay`, `HourBegin`,
`HourEnd`, `CategoryID`, `CategoryDescShort`, `ApproveID`, `DataApprove`, `EmplID`, `NameComplete`,
`SourceCode` (default **`ETIMEM`**), `Note`, `ApproveReply`, `StatusCode` (default **`REQUEST`**),
`AppAbsenceRequestID`, `WriteLanguage`, `AppUpdateDate`, `AppInsertDate`, `UserTZ`, `Delete`
(default 0). Quindi: una richiesta nasce **`REQUEST`** (approvazione in Ecos) salvo scrivere
`StatusCode`/`ApproveID`/`DataApprove` (❓ da provare se accettati in insert). La causale è
**`CategoryID`** (id), non il codice: si prende da `AnagTSCategoryGetAll`.

**Enabled users: nessuno** — nemmeno `maria.carretta`. Serve `PeopleAbsenceRequestMSS` con diritto 4
**e** livello **3 - Manager** per `api.it` (oggi è livello 2).

## `Timesheet2GetAll` — NON è quello che dice la guida (Service `TimesheetAnalysis`, livello 2)

Descrizione della scheda: **ore del timesheet di progetto** per persona (`EmplID`), giorno
(`TSDate`), progetto (`ActivityID`), ore effettive (`HourWork`). **Aggiornato di notte** (i dati
valgono fino al giorno prima), riepilogo per progetto senza dettaglio task, tabella normalizzata
rigenerata ogni notte **senza flag `Delete`**. Le assenze ci passano come `Type`/`CategoryCode`,
ma è un estratto del timesheet, non delle richieste.
**Campi (14)**: `EmplID`, `EmplCode`, `NameComplete`, `EmplExternalCode`, `TSDate`, `SourceCode`,
`Type`, `CategoryCode`, `ActivityID`, `ActivityCode`, `CausalCategoryExternalCode`, `HourWork`,
`PayrollCode`, `YearMonthS`. **Filtri (11)**: `ActivityCode`, `ActivityID`, `CategoryCode`,
`EmplCode`, `EmplID`, `MonthI`, `TSDate`, `Type`, `UpdateDate`, `YearI`, `YearMonthS`.
**Enabled users: nessuno.**

## `PeopleTimePresenceExpectGetAll` — l'orario teorico e il conteggio di Ecos (Service `PersonWorkshift`, livello 2)

«Utilizzata per estrarre l'orario teorico di lavoro», chiavi `EmplID`+`TSDate`. È il **cartellino
calcolato da Ecos**, giorno per giorno: `WorkHourBegin`/`WorkHourEnd`, `WorkHourBeginPause`/
`WorkHourEndPause`, `WeekDayCode`, `WorkshiftCode` (turno), **`WorkHourExpected`** (ore attese),
**`WorkHourReal`** (ore da timbrature), **`WorkHourRegular`** (ore ordinarie),
**`OverTimeHourTotal`** (straordinari), `AbsenceHour`, `VacationHour`, `IllnessHour`,
`MaternityHour`, `AccidentHour`, `OtherAbsenceHour`, `SupplementHourExpect`, `CdCCode`,
**`DepartmentID`/`DepartmentCode`/`DepartmentDescShort`** (il reparto della persona), `YearMonthS`,
`WeekDayDescLong`, `UpdateDate` (27 campi). **Filtri (11)**: `CompanyCode`, `DepartmentCode`,
`DepartmentID`, `EmplCode`, `EmplID`, `InsertDate`, `ReadLanguage`, `TSDate`, `UpdateDate`,
`YearMonthS`, `YearWeekID`. Criteria `ExcludeWFCount=0`.
📌 È il **confronto naturale col nostro motore** (ore attese, ordinarie, straordinario secondo Ecos)
e la fonte del reparto. Diritto di `api.it` ❓ da calibrare.

## `AnagTSCategoryGetAll` — le causali (Service `TimesheetCausal`, livello 2)

«Elenco dei tipi di richiesta assenza/straordinario (attivi e non)». **Campi (12)**: `CategoryID`,
`CategoryCode`, `DescShort`, `TSType`, `isAbsence`, `RowOrder`, `StatusCode`, `FullDayDefault`,
`OverTime`, `UpdateDate`, `SmartWork`, `RequestNoteRequired`. **Filtri (2)**: `ReadLanguage`,
`UpdateDate`. È da qui che si prende il **`CategoryID`** per `PeopleAbsenceRequestPost`.
Diritto di `api.it` ❓ da calibrare.

## `PeopleStampPeriodDayGetAll` — un giorno = una riga, per 90 giorni (Service `PersonStamp`, livello 2)

«Per ogni giorno/persona le ore da timbratura e le ore teoriche di lavoro», chiavi `EmplID`+`StampDate`,
criteria **`StampDate>=oggi-90 AND StampDate<oggi`** (per data del giorno, non per `UpdateDate`).
**Campi (18)**: `EmplID`, `StampDate`, `YearMonthS`, `EmplCode`, `DepartmentID`, `WorkHourExpected`
(ore teoriche da contratto), `DayHoursI` (ore timbrate, decimali), `DepartmentCode`, **`Stamp1`…`Stamp6`**
(gli orari timbrati del giorno, fino a sei), `CdCCode`, `Abnormal`, `TotOvertime`, `TotSupplement`.
**Filtri (11)**: `Abnormal`, `CompanyCode`, `DepartmentCode`, `DepartmentID`, `EmplID`, `MonthI`,
`PayrollCode`, `StampDate`, `UpdateDate`, `YearI`, `YearMonthS`.
📌 È la **fotografia per giorno** che manca a `PeopleStampGetAll`: 90 giorni per data vera, senza
`StampID` però (solo gli orari). Candidata per la risincronizzazione di un mese e per il confronto
col nostro cartellino. Diritto di `api.it` ❓.

## `PeopleOvertimeRequestPost` — richiesta di straordinario (Service `PeopleOvertimeApproveMSS`, Post/Put)

**Campi (21)**: `PeopleOvertimeRequestID` (chiave), `EmplID`, `OvertimeDate`, `ActivityID`,
`OvertimeHourNumber`, `ActivityTaskID`, `CategoryID`, `HourBegin`, `DescLong`, `OvertimeSiteCode`
(default `COMPANY`), `ApproveReply`, `StatusCode` (default `REQUEST`), `Delete` (default 0),
`WriteLanguage`, `UserTZ`, `SourceCode` (default `ETIME`), `AppRequestID`, `AppUpdateDate`,
`AppInsertDate`, `ApproveID`, `DataApprove`. Livello richiesto non indicato nella scheda.

## `PeopleBadgeGetAll` — badge (Service `Badge`, modulo «Servizi generali / Beni&Benefit», livello 2)

Fuori dalla lista della Library ma con la sua scheda. **Campi (15)**: `EmplID`, `StartDate`,
`PeopleBadgeID`, `EmplCode`, `BadgeTypeID`, `BadgeCode`, `NameComplete`, `BirthDate`, `BadgeTypeCode`,
`BadgeTypeDescShort`, `UpdateDate`, `StatusCode`, `EnableGuest`, `CompanyCode`, `InForce`.
**Filtri (5)**: `BadgeCode`, `BadgeTypeCode`, `CompanyCode`, `EmplID`, `UpdateDate`. Nessun criterio
implicito: si chiede tutto (come facciamo, dal 1900).

## `PeopleEmploymentGetALL` — le persone in forza (Service `PersonalData`, livello 2)

Criteria: in forza, o cessate da meno di 3 mesi. **90 campi**, fra cui: `EmplID`, `EmplCode`,
`NameFirst`, `NameLast`, `NameComplete`, `CF`, `Gender`, `BirthDate`, `CompanyCode`,
`DepartmentDescShort`, `CdCCode`/`CdCDescShort`, `DefaultEMailAddress`, `CompanyMobile`,
`PartTimeTypeDescShort`, `ParttimePercent`, `PersonStatusCode`, `HireDate`, `TerminationDate`,
`ContractEndDateD`, `JobCodeCode`/`JobCodeDescShort`, `LevelID`/`LevelDescShort`,
`CategoryCode`/`CategoryDescShort`, `ContractCode`/`ContractDescShort`, `PositionCode`,
`TeamLeaderID`/`TeamLeaderCode`/`TeamLeaderNameFirst`/`TeamLeaderNameLast`, `BranchCode`,
`PayrollCode`, `TSType`, indirizzi (legale, postale, di casa), dati di nascita e cittadinanza,
`UpdateDate`. **Filtri (8)**: `CompanyID`, `EmplID`, `LocationID`, `PersonTypeCode`, `ReadLanguage`,
**`SyncUpdateDate`**, `TerminationDate`, `UpdateDate`. ⚠️ Contiene dati personali ben oltre il
necessario (indirizzi, nascita): se si usa, `ResultFields` con i soli campi che servono.

## `PeopleExpressLightGetAll` — anagrafica leggera per integrazioni (Service `PeopleExpressLight`, livello 2)

«Nome, cognome, ruolo, data di assunzione, unità organizzativa; senza documenti, abilitazioni,
dati retributivi». Criteria `inForce=1`. **Campi (21)**: `EmplID`, `NameComplete`, `CF`,
`CompanyCode`, `CompanyDescShort`, `DepartmentDescShort`, `DepartmentCode`, `PhoneOffice`,
`PhoneCompany`, `LegalAddressComplete`, `LocationName`, `LocationCode`, `LocationAddressComplete`,
`DefaultEMailAddress`, `HireDate`, `JobCodeCode`, `JobCodeDescShort`, `BranchDescShort`, `BranchCode`,
`TeamLeaderNameComplete`, `WorkSiteAddressComplete`. 🪤 **Non ha `EmplCode`**: per il ponte
`EmplID`↔`EmplCode` serve `PeopleEmploymentGetALL` o `PeopleBadgeGetAll` (che li ha entrambi).
**Filtri (6)**: `EmplID`, `HireDate`, `ReadLanguage`, `SourceUpdateDate`, `SyncUpdateDate`,
`UpdateDate`. ❌ negata a `api.it`.

## `AnagDepartmentGetAll` — reparti (Service `Department`, livello 2)

**Campi (21)**: `DepartmentID`, `DepartmentCode`, `EffDate`, `BranchID`, `BranchDescShort`,
`BranchCode`, `DescShort`, `DescLong`, `ManagerID`, `ManagerCode`, `ManagerNameComplete`
(il responsabile del reparto), `FatherID`/`FatherCode`/`FatherDescShort` (gerarchia), `TypeDescShort`,
`StatusCode`, `StatusDescShort`, `InsertDate`, `UpdateDate`, `RowOrder`, `RootLevel`.
**Filtri (6)**: `BranchCode`, `BranchID`, `InsertDate`, `ReadLanguage`, `StatusCode`, + 1 non letto.

## Sei API fuori dalla Library, lette dalla scheda dopo la tendina del Test Panel

| ApiName | Service · livello | Criteria | Campi | Filtri ammessi |
|---|---|---|---|---|
| **`StampPresenceMonthCardGetAll`** «informazione presa dal cartellino persone» | PersonStamp · 2 | **nessuno** (❓ forse tutta la storia, per `YearMonthS`) | `EmplID`, `StampDate`, `YearMonthS`, `PresenceHour`, `WorkHourExpectTotalTime`, `WorkHourExpectedRealTime`, `DeltaHourDayTime`, `DayHourList`, **`StampCode1`…`StampCode8`**, `WorkshiftCode`, `Abnormal`, `ToApprove`, `PeopleExpenseIndemnityList` | `Abnormal`, `EmplID`, `StampDate`, `YearMonthS` |
| **`PeopleAbsenceRequestRefineWorkAll`** (la richiesta «raffinata» giorno per giorno) | PeopleAbsenceRequest · 2 — **`api.it` ce l'ha già** | nessuno | `AbsenceRequestRefineID`, `AbsenceRequestID`, `EmplID`, `EmplCode`, `NameComplete`, **`TSDate`**, `HourBegin`, `HourEnd`, `StatusCode`, `SourceCode`, `CompanyCode`, `CompanyDescShort`, `CategoryCode`, `CategoryDescShort`, `UpdateDate` | `AbsenceRequestID`, `CategoryCode`, `CompanyCode`, `EmplID`, `ReadLanguage`, `SourceCode`, `StatusCode`, **`TSDate`**, `UpdateDate` |
| `PeopleWorkHourMonthGetAll` (totali del mese) | PersonWorkshift · 2 | nessuno | `EmplID`, `YearI`, `MonthI`, `WorkHourExpected`, `WorkHourReal`, `OvertimeHour`, `AbsenceHour`, `OvertimeForfaitHourSum`, `VacationHour`, `IllnessHour`, `MaternityHour`, `AccidentHour`, `OtherAbsenceHour` | `EmplID`, `MonthI`, `YearI` |
| `HolidayPeopleGetAll` (festività per persona) | PersonWorkshiftESS · 2 | da −12 a +15 mesi, ultima versione valida | `EmplID`, `YearI`, `MonthI`, `DayI` | `DayI`, `EmplID`, `MonthI`, `YearI` |
| `PeopleWorkShiftPlanGetAll` (turni pianificati) | PersonWorkScheduleMSS · 2 | ultima `EffDate` per persona/data/giorno non cancellata | `EmplID`, `UniqueID`, `EffDate`, `EndDate`, `StartDate`, `DayCode`, `HourBegin`, `HourEnd`, `HourBeginPause`, `HourEndPause`, `BreakTime`, `DepartmentID`/`Code`/`DescShort`, `StatusCode`, `Delete`, `Note`, `UpdateDate`, `UpdateEmplID`, `UpdateNameComplete`, `UserTZ`, `YearMonthS`, `StartYearWeekID` | `CompanyID`, `DepartmentID`, `EmplID`, `EmploymentDepartmentID`, `StartDate` |
| `PeopleBadgeStampGetAll` (solo badge di timbratura di persone non cessate) | Badge · 2 | `(BadgeTypeCode='TIMBR' OR IsStampCard=1) AND (TerminationDate is NULL or > oggi−1 mese)` | `EmplID`, `StartDate`, `PeopleBadgeID`, `EmplCode`, `BadgeCode`, `NameComplete`, `BirthDate`, `StatusCode`, `EnableGuest`, `LocationID`, `CompanyCode`, `InForce` | `LocationID`, `PeopleBadgeID`, `UpdateDate` |

📌 Due piste che cambiano le carte: **`PeopleAbsenceRequestRefineWorkAll`** dà le assenze
**per giorno** (`TSDate`, ore inizio/fine, causale, stato) senza finestra implicita e con un
servizio che `api.it` ha già — è la riconciliazione per giornata che ci manca;
**`StampPresenceMonthCardGetAll`** è il cartellino giornaliero di Ecos con fino a otto orari e
nessun criterio implicito: da calibrare con `api.it` (servizio `PersonStamp`, non `PeopleStamp`)
per capire se restituisce anche la storia oltre i 60 giorni. `ServerTimeGet` (servizio
`ServerTime`, livello 6 External User, campi `ServerDateTime` e `ServerTZ`) dà l'orologio di
Ecos: utile per il cursore. `StampCausalGetALL` (PersonStamp): `StampCausalID`, `StampCausalCode`,
`DescShort`, `StatusCode`, `RowOrder`, `DefaultValue`, `Delete`, `UpdateDate`.

---

# Tendina dell'API Test Panel — tutte le 230 API GET del tenant (08/09/2026)

> Letta dopo il `TokenGet` di Diego nel pannello. Sono **solo le API di lettura** (il pannello
> chiama «Get Api Data»): le `Post*` non ci sono. Le 84 della Library sono un sottoinsieme.
> In **grassetto** quelle che riguardano presenze e persone e che non avevamo mai visto.

**Timbrature e presenze**: PeopleStampActiveGetESS, PeopleStampDenialGetAll, PeopleStampGetAll,
PeopleStampGetESS, PeopleStampGetMSS, PeopleStampGetPSS, PeopleStampPeriodDayGetAll,
PeopleStampPeriodDayGetESS, PeopleStampPeriodGetAll, **PeopleBadgeStampGetAll**,
PeopleBadgeStampGetPSS, **PeopleBadgeCheckGet**, PeopleBadgeCheckGetPSS, PeopleBadgeMeetingCheckGet,
PeopleBadgeGetAll, ActivityPeopleStampGetESS, **StampCausalGetALL**, StampCausalGetESS,
**StampDeviceGetAll**, StampDeviceGetPSS, **StampPresenceMonthCardGetAll**,
PeopleTimePresenceExpectGetAll, PeopleTimePresenceExpectGetESS,
PeopleTimePresenceExpectEventGETESS, PeopleTimePresenceExpectEventGETPSS,
**PeopleWorkHourMonthGetAll**, **PeopleWorkShiftPlanGetAll**, PeopleWorkShiftPlanGetPSS,
PeopleWorkGetESS, PeopleTimesheetGetESS, **ServerTimeGet**, DeviceAccessLimitationGetAll,
DeviceSirenTimeGetAll, BeaconGetAll.

**Assenze, ferie, straordinari, festività**: PeopleAbsenceDayGetAll, PeopleAbsenceRequestGetAll,
PeopleAbsenceRequestGetESS, PeopleAbsenceRequestGetMSS, PeopleAbsenceRequestGetPSS,
**PeopleAbsenceRequestRefineWorkAll**, PeopleOvertimeRequestGetAll, PeopleSmartWorkRequestGetAll,
PeopleAccidentAbstentionGetAll, PeopleDismissalAbstentionGetAll, PeopleDismissalGetAll,
**HolidayPeopleGetAll**, HolidayPeopleGetPSS, AnagTSCategoryGetAll, AnagTSCategoryGetESS,
TSCategoryOverlapCheckGetESS, **PeopleCalendarGetMSS**.

**Persone, reparti, organigramma**: PeopleEmploymentGetALL, PeopleEmploymentGetMSS,
PeopleEmploymentGetPSS, **PeopleEmploymentLightGetALL**, PeopleExpressGetAll, PeopleExpressLightGetAll,
PeopleExpressMidGetAll, PeopleJobGetAll, PeopleJobMidGetAll, PeopleJobMonthHistoryGetAll,
**PeopleSearchGetAll**, PeoplePhoneGetAll, PhonebookGetAll, PeopleSetGetESS, PeopleSectionGenGetESS,
PeopleCompanyTaskGetAll, PeopleCompanyOwnGetAll, PeopleCompetenceGetAll, PeopleCompetenceHistoryGetAll,
PeoplePhysicExamGetAll, PeopleSkillGetMSS, PeopleEvaluationGetMSS, PeopleDocumentGetMSS,
PeopleDocumentGetListMSS, PeopleBusinessCardGetESS, PeopleBusinessCardGetPSS, PeopleNotificationGetESS,
PeopleNotificationGetListESS, AnagDepartmentGetAll, **DepartmentListGetPSS**, **DepartmentPeopleGetPSS**,
**OrgChartTreeLastGetALL**, ManagerSecurityListMSS, AnagPositionGetAll, AnagPositionPeopleGetAll,
AnagJobCodeGetAll, AnagJobCodeCompetenceGetAll, AnagBranchGetAll, AnagCompanyGetAll,
AnagCompanyLightGetAll, AnagLocationGetAll, AnagLocationGetESS, AnagCountryGetAll, AnagStateGetAll,
AnagGenGetAll, AnagCurrencyGetAll, CurrencyExchangeGetAll, TextGENGetAll.

**Paghe e compensi**: PayrollChangeGetAll, PayrollCostDetailGetAll, PayrollResultGetAll,
PayrollResultLastGet, PeopleCompensationCurrentGetAll, PeopleCompensationMonthHistoryGetAll,
PeopleCompensationProjectCurrentGetAll, PeopleCompensationProjectMonthHistoryGetAll,
PeopleCompensationRateCodeMonthHistoryGetAll, SalaryPlanRequestGetAll, TicketRestaurantAccruedGetAll.

**Progetti e timesheet**: ActivityBModelGetAll, ActivityBModelOtherCostGetAll,
ActivityBModelOtherRevenueGetAll, ActivityBModelResourceGetAll, ActivityBModelTrancheGetAll,
ActivityConsumptiveEconomicsMonthGetAll, ActivityGroupGetAll, ActivityPeopleGetAll, ActivityPeopleGetESS,
ActivityPeopleGetMSS, ActivityPeopleLightGetAll, ActivityPeopleTaskWBSGetAll, ActivityPeopleTaskWBSGetESS,
ActivityProjectTrancheGetAll, ActivityTaskWBSPeoplePlanGetAll, ActivityWBSTaskAdvanceGetAll,
ActivityWBSTaskGetAll, AnagActivityGet, AnagActivityOpportunityGetAll, AnagActivityOtherCostGetAll,
AnagActivityOtherReturnGetAll, AnagActivityProjectGet, AnagActivityProjectTypeGetAll, AnagProductGetAll,
BudgetModelVersionValueGetAll, ProjectEconomicsGetAll, ProjectWBSEconomicsGetAll, Timesheet2GetAll,
TimesheetActivityTask2GetAll, TimesheetActivityTaskGetAll, TimesheetGetAll, TimesheetListGetAll,
ProductBacklogItemGetAll, ProductBacklogItemGetESS, ProductBacklogItemCommunicationGetESS,
ProductRequirementGetAll.

**Spese e trasferte**: AnagExpenseCarGroupGetAll, AnagExpenseCarGroupGetESS, AnagExpenseTypeGetAll,
AnagExpenseTypeGetESS, AnagPaymentTypeGetESS, CompanyOwnCarPoolGetESS, CreditCardBalanceTransactionCleanGetESS,
CreditCardResidualGetESS, CreditCardTransactionGetAll, ExpensePictureGetMSS, PeopleExpenseAdvanceApproveGetMSS,
PeopleExpenseAdvanceGetESS, PeopleExpenseApproveGetMSS, PeopleExpenseByTypeGetMSS, PeopleExpenseCarGet,
PeopleExpenseCarGetESS, PeopleExpenseGeneralGet, PeopleExpenseGeneralSplitGetESS, PeopleExpenseGetAll,
PeopleExpenseGetESS, PeopleExpenseGetFullESS, PeopleExpenseGetMSS, PeopleExpenseIndemnityGet,
PeopleExpenseIndemnityGetESS, PeopleExpenseReimbursementYearMonthGetAll, PeopleTravelApproveGetMSS,
PeopleTravelAttachmentListFullGetAll, PeopleTravelAttachmentListGetAll, PeopleTravelGetESS,
PeopleTravelSetGetESS, TransferTypeGetAll, TransferTypeGetESS.

**CRM, acquisti, beni**: AccountCRMDataGetAll, AnagAccountClientAPPGetESS, AnagAccountClientLogoGet,
AnagAccountCurrentGetAll, AnagAccountCurrentGetESS, AnagAccountGetAll, AnagAccountGetOne,
AnagAccountReferenceGetAll, MarketAreaGetAll, MarketGetAll, OrderPurchaseGetAll, NewAccountClientSet,
CompanyOwnEquipmentControlGetAll, CompanyOwnGetAll, CompanyOwnGetFullAll, AnagCdCGettAll.

**Recruiting, formazione, valutazione**: AnagCompetenceGetAll, AnagCourseCompetenceGetAll,
AnagInterviewCompetenceGetAll, AnagInterviewGetAll, AnagJobRequisitionGetAll, AnagVideoInterviewGet,
AppraisalPeopleEvaluationGetAll, CVExternalFormSettingGetAll, CVGetAll, CVLimitedGetAll,
CVQuestionAnswerGetAll, CVSourceGetAll, JobRequisitionActiveGet, JobRequisitionCareerSiteGet,
JobRequisitionCVGetAll, JobRequisitionTagGet, PeopleCourseCompanyGetAll, RecruitingSettingCompanyGetAll,
RecruitingSettingGetAll, VideoInterviewRunAnswerGetAll, VideoInterviewRunGetAll,
VideoInterviewTemplateQuestionGetAll.

**Altro**: AdvertsBoardGetAll, AdvertsBoardGetListAll, APPMobileGetAll, ApplicationClientEvaluateGetAll,
ClientAPPModuleGetESS, EcosAgileAIBotDataGetAll, GreenPassControlGetAll, GreenPassControlGetPSS,
GreenPassSetGetAll, MeetingGetAll, MeetingPeopleGetAll, MeetingRoomDeviceGetPSS, MeetingRoomGetAll,
TokenGet.
