# ============================================================
# Esegui questi comandi DENTRO Claude Code (uno alla volta)
# ============================================================

# --- WPF Dev Pack (57 skill, 11 agenti, 5 comandi WPF) ---
/plugin marketplace add christian289/dotnet-with-claudecode
/plugin install wpf-dev-pack@dotnet-with-claudecode

# --- Interface Design (design system persistente) ---
/plugin marketplace add Dammyjay93/interface-design
/plugin install interface-design@interface-design

# --- dotnet-skills (30 skill .NET: C#, testing, patterns) ---
/plugin marketplace add Aaronontheweb/dotnet-skills
/plugin install dotnet-skills@dotnet-skills

# ============================================================
# MCP Server: C# LSP (IntelliSense + XAML diagnostics)
# Esegui nel terminale PRIMA di aggiungere a Claude Code:
#
#   git clone https://github.com/HYMMA/csharp-lsp-mcp.git C:\Tools\csharp-lsp-mcp
#   cd C:\Tools\csharp-lsp-mcp\src\CSharpLspMcp
#   dotnet publish -c Release -o C:\Tools\csharp-lsp-mcp\publish
#
# Poi in Claude Code:
#   claude mcp add csharp-lsp -- C:\Tools\csharp-lsp-mcp\publish\CSharpLspMcp.exe
# ============================================================
