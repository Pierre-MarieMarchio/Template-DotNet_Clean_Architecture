namespace AppTemplate.PresentationAppMcp.UnitTests.TestSupport;

internal static class ToolCatalogue
{
    internal static IReadOnlyList<string> ReadOnlyNames { get; } =
    [
        //#if (todoLists)
        "list_todo_lists",
        "get_todo_list",
        //#endif
        //#if (reminders)
        "list_reminders",
        //#endif
        //#if (files)
        "list_files",
        "get_file",
        //#endif
        //#if (notes)
        "list_notes",
        "get_note",
        //#endif
    ];

    internal static IReadOnlyList<string> WritingNames { get; } =
    [
        //#if (todoLists)
        "create_todo_list",
        "add_todo_item",
        //#endif
        //#if (reminders)
        "schedule_reminder",
        //#endif
        //#if (notes)
        "create_note",
        //#endif
    ];

    internal static IReadOnlyList<string> Names { get; } = [.. ReadOnlyNames, .. WritingNames];
}
