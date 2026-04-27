namespace GitUI.Models;

public record RepoItem(
    long Id,
    string Name,
    string FullName,
    string? Description,
    bool Private,
    string HtmlUrl,
    string DefaultBranch);
