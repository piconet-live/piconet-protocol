using System.Reflection;

namespace Online.Protocol;

/// <summary>A library macro (the language's section 9): its parameters (kind <c>time</c>, <c>int</c> or <c>block</c>,
/// and name) and its body's tokens, which a use puts in its place with the arguments' tokens for the parameters.</summary>
internal sealed record ModeMacro(string Name, IReadOnlyList<(string Kind, string Name)> Params, IReadOnlyList<ModeToken> Body);

/// <summary>The standard library of the api this build compiles: <c>std.fzmode</c>, written in the language and
/// compiled with every mode file (the language's section 9). Its macros are expanded where a file uses them; its subs
/// are the file's too, written only where they are called, so the library costs a mode nothing it does not use.</summary>
internal static class ModeLibrary
{
    private static readonly Lazy<(Dictionary<string, ModeMacro> Macros, List<ModeSubItem> Subs)> Std = new(() => Read(Text()));

    public static IReadOnlyDictionary<string, ModeMacro> Macros => Std.Value.Macros;

    public static IReadOnlyList<ModeSubItem> Subs => Std.Value.Subs;

    /// <summary>The library's text, as the build carries it.</summary>
    public static string Text()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Online.Protocol.Modes.std.fzmode")
            ?? throw new InvalidOperationException("the build carries no std.fzmode");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>A library's macros and subs; a library that does not read throws (it is the build's, never a
    /// player's).</summary>
    public static (Dictionary<string, ModeMacro> Macros, List<ModeSubItem> Subs) Read(string text)
    {
        try
        {
            return ModeParser.Library(ModeLexer.Tokens(text, []));
        }
        catch (ModeCompileException error)
        {
            throw new InvalidOperationException($"the standard library does not read: {ModeCompiler.Message(error.Line, error.Message)}", error);
        }
    }
}
