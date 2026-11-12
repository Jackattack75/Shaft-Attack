#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

// Temporary (Claude): writes compile results to Assets/ShaftAttack/_ClaudeShots~/compile_log.txt. Delete when done.
[InitializeOnLoad]
internal static class ClaudeCompileLog
{
    static ClaudeCompileLog()
    {
        CompilationPipeline.assemblyCompilationFinished -= Done;
        CompilationPipeline.assemblyCompilationFinished += Done;
    }

    private static void Done(string assembly, CompilerMessage[] messages)
    {
        string dir = Path.Combine(Application.dataPath, "ShaftAttack/_ClaudeShots~");
        if (!Directory.Exists(dir)) return;
        int errors = 0;
        string text = "";
        foreach (CompilerMessage m in messages)
        {
            if (m.type != CompilerMessageType.Error) continue;
            errors++;
            text += "  " + m.file + ":" + m.line + " " + m.message + "\n";
        }
        File.AppendAllText(Path.Combine(dir, "compile_log.txt"),
            System.DateTime.Now.ToString("HH:mm:ss") + " " + Path.GetFileName(assembly) + " errors " + errors + "\n" + text);
    }
}
#endif
