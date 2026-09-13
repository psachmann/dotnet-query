namespace DotNetQuery.Mvvm;

// Shared parameter-cast logic for MutationExecuteCommand and the ToCommand-produced commands.
// Nothing is converted — XAML string-to-int coercion is the binding's job — so this only ever
// does a direct runtime type check, which stays trim/AOT safe.
internal static class CommandParameterCasting
{
    // Strict: throws with both the expected and actual type named, for use from Execute.
    public static T Cast<T>(object? parameter, string commandName)
    {
        if (TryCast<T>(parameter, out var typed))
        {
            return typed;
        }

        throw new ArgumentException(
            $"{commandName} expected a parameter of type '{typeof(T)}' but received "
                + (parameter is null ? "'null'." : $"'{parameter.GetType()}'."),
            nameof(parameter)
        );
    }

    // Lenient: for use from CanExecute, where a framework may probe with a not-yet-set parameter
    // and a false result — not an exception — is the correct way to say "not executable yet".
    public static bool TryCast<T>(object? parameter, out T value)
    {
        if (parameter is T typed)
        {
            value = typed;
            return true;
        }

        if (parameter is null && default(T) is null)
        {
            value = default!;
            return true;
        }

        value = default!;
        return false;
    }
}
