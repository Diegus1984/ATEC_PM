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
