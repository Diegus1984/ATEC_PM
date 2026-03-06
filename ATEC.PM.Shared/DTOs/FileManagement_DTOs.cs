namespace ATEC.PM.Shared.DTOs;

public class SubfolderRequest
{
    public string SubPath { get; set; } = "";
    public string FolderName { get; set; } = "";
}

public class RenameRequest
{
    public string OldPath { get; set; } = "";
    public string NewName { get; set; } = "";
}

public class DeleteItemRequest
{
    public string ItemPath { get; set; } = "";
}

public class MoveItemRequest
{
    public string SourcePath { get; set; } = "";
    public string DestinationFolder { get; set; } = "";
}


public class FileItem
{
    public string Name { get; set; } = "";
    public bool IsFolder { get; set; }
    public long Size { get; set; }
    public string RelativePath { get; set; } = "";
    public DateTime? Modified { get; set; }
}

public class FileTreeItem
{
    public string Name { get; set; } = "";
    public bool IsFolder { get; set; }
    public long Size { get; set; }
    public string RelativePath { get; set; } = "";
    public DateTime? Modified { get; set; }
    public List<FileTreeItem> Children { get; set; } = new();
}