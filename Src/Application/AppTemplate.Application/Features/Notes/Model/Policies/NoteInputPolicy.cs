namespace AppTemplate.Application.Features.Notes.Model.Policies;

public static class NoteInputPolicy
{
    public const int TitleMaxLength = 200;

    public const int BodyMaxLength = 10_000;
}
