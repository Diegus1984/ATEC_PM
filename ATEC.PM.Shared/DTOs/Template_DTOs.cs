using System;
using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

public class TemplateFolderNode
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public List<TemplateFolderNode> Children { get; set; } = new();
    public List<TemplateFileItem> Files { get; set; } = new();
}

public class TemplateFileItem
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime UploadedAt { get; set; }
}

public class CreateTemplateFolderRequest
{
    public int? ParentId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

public class UpdateTemplateFolderRequest
{
    public string Name { get; set; } = "";
    public int? ParentId { get; set; }
    public int SortOrder { get; set; }
}

public class ReorderItem
{
    public int Id { get; set; }
    public int SortOrder { get; set; }
}

public class UpdateTemplateFileRequest
{
    public string Name { get; set; } = "";
}

public class MoveOrCopyFileRequest
{
    public int FolderId { get; set; }
}
