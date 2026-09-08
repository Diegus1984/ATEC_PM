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
| `PeopleGetAll`, `PeopleAbsenceTypeGetAll`, `PeopleDepartmentGetAll`, `Timesheet2GetESS` | `-99` | non esistono (i nomi giusti sono nella tabella sopra) |
