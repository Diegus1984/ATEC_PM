# Backup — Configurazione Sezioni Costo (pre Shadcn preset bIkeymG)

**Data:** 2026-06-03  
**Preset di riferimento:** [ui.shadcn.com/create?preset=bIkeymG](https://ui.shadcn.com/create?preset=bIkeymG)

Copia dei file originali prima del restyling Shadcn.  
**Cartella fuori da `ATEC.PM.Client`** per non essere compilata da MSBuild.

## Ripristino rapido (PowerShell)

```powershell
$src = "c:\Users\diego\Desktop\ATEC_PM_CSharp_v5\ATEC_PM\backups\ConfigurazioneSezioni_pre_shadcn_bIkeymG_2026-06-03"
$dst = "c:\Users\diego\Desktop\ATEC_PM_CSharp_v5\ATEC_PM\ATEC.PM.Client\Views\ConfigurazioneSezioni"
Copy-Item "$src\CostSectionsTreePage.xaml" $dst -Force
Copy-Item "$src\CostSectionsTreePage.xaml.cs" $dst -Force
Copy-Item "$src\CostSectionTemplateDialog.xaml" $dst -Force
Copy-Item "$src\CostSectionTemplateDialog.xaml.cs" $dst -Force
Copy-Item "$src\DepartmentDialog.xaml" $dst -Force
Copy-Item "$src\DepartmentDialog.xaml.cs" $dst -Force
```

Poi: `dotnet build` sul client.
