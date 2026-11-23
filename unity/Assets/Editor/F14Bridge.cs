using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

// Dev-only bridge used while building the F-14B mod. Lives in Assets/Editor, never shipped.
// Polls E:/Project/NOModding/F14Bridge for refresh.flag (AssetDatabase.Refresh) and job.txt
// (lines "Namespace.Type.Method" -> invoke public static method). Everything is logged to log.txt.
[InitializeOnLoad]
public static class F14Bridge
{
    public const string Dir = "E:/Project/NOModding/F14Bridge";
    static double next;

    static F14Bridge()
    {
        Directory.CreateDirectory(Dir);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        CompilationPipeline.assemblyCompilationFinished -= OnCompiled;
        CompilationPipeline.assemblyCompilationFinished += OnCompiled;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        Log("bridge loaded");
    }

    static void OnCompiled(string asm, CompilerMessage[] msgs)
    {
        foreach (var m in msgs)
            if (m.type == CompilerMessageType.Error) Log("COMPILE ERROR " + m.file + ":" + m.line + " " + m.message);
    }

    static void OnLog(string cond, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            Log("CONSOLE " + type + ": " + cond + (type == LogType.Exception ? "\n" + stack : ""));
        else if (cond.StartsWith("[F14]")) Log(cond);
    }

    public static void Log(string s)
    {
        try { File.AppendAllText(Dir + "/log.txt", DateTime.Now.ToString("HH:mm:ss ") + s + "\n"); } catch { }
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1.0;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            File.WriteAllText(Dir + "/heartbeat.txt", DateTime.Now.ToString("o"));
            string rf = Dir + "/refresh.flag";
            if (File.Exists(rf))
            {
                File.Delete(rf);
                Log("refresh");
                AssetDatabase.Refresh();
                return;
            }
            string job = Dir + "/job.txt";
            if (!File.Exists(job)) return;
            var lines = File.ReadAllLines(job);
            File.Delete(job);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                int dot = line.LastIndexOf('.');
                string tn = line.Substring(0, dot), mn = line.Substring(dot + 1);
                Type t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(tn)).FirstOrDefault(x => x != null);
                if (t == null) { Log("JOB type not found: " + tn); continue; }
                var mi = t.GetMethod(mn, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (mi == null) { Log("JOB method not found: " + line); continue; }
                Log("JOB start " + line);
                try { mi.Invoke(null, null); Log("JOB done " + line); }
                catch (TargetInvocationException e) { Log("JOB EXCEPTION " + line + ": " + e.InnerException); }
            }
        }
        catch (Exception e) { Log("bridge error " + e); }
    }
}
