using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DnWModLoader.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DnWModLoader
{
    public static partial class ModLoader
    {
        private static readonly List<string> SetupProblems = new List<string>();

        internal static void Initialize()
        {
            if (Phase != LoaderPhase.NotStarted) return;
            SetPhase(LoaderPhase.Initializing);
            var stopwatch = Stopwatch.StartNew();

            ResolvePaths();
            CreateDirectory(ModsDirectory);
            CreateDirectory(ConfigDirectory);
            string fallbackDirectory = FallbackDirectory();
            Log.Open(Path.Combine(ModsDirectory, LogFileName), fallbackDirectory != null ? Path.Combine(fallbackDirectory, LogFileName) : null);
            AssemblyResolver.Logger = Logger;
            if (Log.FilePath != null && !Log.IsFallback) RemoveFallbackLogs(fallbackDirectory);

            Config = LoaderConfig.Load(Path.Combine(ModsDirectory, LoaderConfig.FileName), Logger);
            Log.MinimumLevel = Config.LogLevel;
            Log.EchoToUnity = Config.EchoLoaderLogToUnity;

            LogHeader();
            HookUnityLog();
            AssemblyResolver.Install(LoaderDirectory);
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            try { HarmonyLog.Forward(); }
            catch (Exception e) { Logger.Debug("Could not forward HarmonyX's log: " + e.Message); }
            EnsureBehaviour("SubsystemRegistration");

            try
            {
                DiscoverAndLoadMods();
            }
            catch (Exception e)
            {
                Logger.Exception(e, "Mod discovery failed");
            }

            try
            {
                _bepInExPluginsFound = HasBepInExPlugins();
                if (_bepInExPluginsFound) DiscoverBepInExPlugins();
            }
            catch (Exception e)
            {
                Logger.Exception(e, "BepInEx plugin discovery failed");
            }

            try
            {
                WarnAboutMelonPlugins();
                _melonsFound = ModsFolderMelons.Count > 0;
                if (_melonsFound) DiscoverMelons();
            }
            catch (Exception e)
            {
                Logger.Exception(e, "MelonLoader mod discovery failed");
            }

            SetPhase(LoaderPhase.Initialized);
            CountStatuses(out int loaded, out int failed, out int skipped, out int disabled);
            int waiting = ModList.Count(c => c.Framework != null && c.Status == ModStatus.Discovered);
            Logger.Info("Initialization finished in " + stopwatch.ElapsedMilliseconds + " ms: " + loaded + " loaded, " + failed + " failed, " + skipped + " skipped, " + disabled + " disabled"
                        + (waiting > 0 ? ", " + waiting + " hosted plugin(s)/mod(s) found." : "."));

            RaiseModsInitialized();
            HookSceneEvents();

            if (!Preloader.AfterRegistrationHooked)
            {
                StartBepInExPlugins("SubsystemRegistration");
                StartMelons("SubsystemRegistration");
                SetPhase(LoaderPhase.AllModsStarted);
            }
        }

        internal static void AfterRegistration()
        {
            string phase = Preloader.AfterRegistrationPhase ?? "a later initializer";
            EnsureBehaviour(phase);
            StartBepInExPlugins(phase);
            StartMelons(phase);
            if (Phase == LoaderPhase.Initialized) SetPhase(LoaderPhase.AllModsStarted);
        }

        internal static void GameStarted()
        {
            if (Phase >= LoaderPhase.Running) return;
            SetPhase(LoaderPhase.Running);
            Logger.Info("First scene loaded: " + SafeActiveSceneName() + ". Game is running.");
            Dispatch(nameof(Mod.OnGameStarted), GameStartedAction);
            HostHooks.Run(GameStartedHook);
        }

        internal static void Shutdown()
        {
            Logger.Info("Application quitting.");
            if (_melonsFound)
            {
                try { QuitMelons(); }
                catch (Exception e) { Logger.Exception(e, "Shutting down MelonLoader mods failed"); }
            }
            Log.Close();
        }

        private static readonly Action<Mod> GameStartedAction = m => m.OnGameStarted();
        private static readonly Action<HostHooks> GameStartedHook = h => h.GameStarted();

        private static string SafeActiveSceneName()
        {
            try { return SceneManager.GetActiveScene().name; } catch { return "?"; }
        }

        public static string LoaderDirectory { get; private set; }

        private static void ResolvePaths()
        {
            string loaderDir = null;
            try
            {
                string location = typeof(ModLoader).Assembly.Location;
                if (!string.IsNullOrEmpty(location)) loaderDir = Path.GetDirectoryName(Path.GetFullPath(location));
            }
            catch { }
            LoaderDirectory = loaderDir;

            string data = null;
            try { data = Application.dataPath; } catch { }

            string game = null;
            if (Preloader.Invoked && !string.IsNullOrEmpty(Preloader.GameDirectory)) game = Preloader.GameDirectory;
            else if (!string.IsNullOrEmpty(data)) game = Path.GetDirectoryName(Path.GetFullPath(data));
            else if (!string.IsNullOrEmpty(loaderDir)) game = Preloader.GuessGameDirectory(loaderDir);
            if (string.IsNullOrEmpty(game)) game = Path.GetFullPath(".");

            GameDirectory = Path.GetFullPath(game);
            DataDirectory = !string.IsNullOrEmpty(data) ? Path.GetFullPath(data) : (Path.GetDirectoryName(Preloader.FindManagedDirectory(GameDirectory) ?? Path.Combine(GameDirectory, "Data")) ?? GameDirectory);
            ManagedDirectory = Preloader.ManagedDirectory ?? Preloader.FindManagedDirectory(GameDirectory) ?? Path.Combine(DataDirectory, "Managed");
            ModsDirectory = Path.Combine(GameDirectory, ModsFolderName);
            ConfigDirectory = Path.Combine(ModsDirectory, ConfigFolderName);
        }

        private static void CreateDirectory(string directory)
        {
            try { Directory.CreateDirectory(directory); }
            catch (Exception e) { SetupProblems.Add("Could not create " + directory + ": " + e.Message); }
        }

        internal static string FallbackDirectory()
        {
            try
            {
                string persistent = Application.persistentDataPath;
                if (!string.IsNullOrEmpty(persistent)) return Path.GetFullPath(persistent);
            }
            catch { }
            try { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DnWModLoader"); }
            catch { return null; }
        }

        private static void RemoveFallbackLogs(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;
            foreach (var name in new[] { LogFileName, PreviousLogFileName })
            {
                try
                {
                    string path = Path.Combine(directory, name);
                    if (File.Exists(path)) File.Delete(path);
                }
                catch { }
            }
        }

        private static void LogHeader()
        {
            Logger.Info("DnW Mod Loader " + Version + " starting (" + Preloader.Status + ")");
            try
            {
                Logger.Info("Game: " + Application.productName + " " + Application.version + " by " + Application.companyName
                            + " | Unity " + Application.unityVersion + " | " + Application.platform);
            }
            catch (Exception e) { Logger.Debug("Application info unavailable: " + e.Message); }
            if (Direct3D12Warning.Applies()) Logger.Warning(Direct3D12Warning.LogMessage);
            if (ParallelLoaderWarning.Applies()) Logger.Error(ParallelLoaderWarning.LogMessage);
            if (WriteProtectionWarning.Applies()) Logger.Warning(WriteProtectionWarning.LogMessage);
            foreach (var problem in SetupProblems) Logger.Warning(problem);
            try { Logger.Debug("OS: " + SystemInfo.operatingSystem + " | CLR: " + Environment.Version + " | 64-bit: " + Environment.Is64BitProcess); } catch { }
            try { Logger.Debug("Command line: " + string.Join(" ", Environment.GetCommandLineArgs())); } catch { }
            foreach (var line in Preloader.TakeEarlyLog()) Logger.Debug("[preloader] " + line);
            Logger.Debug("Game directory: " + GameDirectory);
            Logger.Debug("Loader directory: " + LoaderDirectory);
            Logger.Debug("Mods directory: " + ModsDirectory);
            Logger.Debug("Log file: " + Log.FilePath);
            Logger.Debug("Config: hotkey=" + Config.OverlayHotkey + " logLevel=" + Config.LogLevel + " mirrorUnityLog=" + Config.MirrorUnityLog
                         + " disabledMods=[" + string.Join(", ", Config.DisabledMods) + "]");
        }

        private static void HookUnityLog()
        {
            try { Application.logMessageReceivedThreaded += OnUnityLogMessage; }
            catch (Exception e) { Logger.Debug("Could not hook Unity log: " + e.Message); }
        }

        private static void OnUnityLogMessage(string condition, string stackTrace, LogType type)
        {
            var mirror = Config.MirrorUnityLog;
            if (mirror == UnityLogMirror.None || Log.IsEchoedLine(condition)) return;
            switch (type)
            {
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    if (mirror >= UnityLogMirror.Error)
                    {
                        string text = condition;
                        if (type == LogType.Exception && !string.IsNullOrEmpty(stackTrace)) text += "\n" + TrimStack(stackTrace, 12);
                        Log.Write(LogLevel.Error, "Unity", text);
                    }
                    break;
                case LogType.Warning:
                    if (mirror >= UnityLogMirror.Warning) Log.Write(LogLevel.Warning, "Unity", condition);
                    break;
                default:
                    if (mirror >= UnityLogMirror.All) Log.Write(LogLevel.Debug, "Unity", condition);
                    break;
            }
        }

        private static string TrimStack(string stack, int maxLines)
        {
            var lines = stack.Replace("\r", "").Split('\n');
            if (lines.Length <= maxLines) return stack.TrimEnd();
            return string.Join("\n", lines.Take(maxLines)) + "\n  ... (" + (lines.Length - maxLines) + " more)";
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try { Logger.Error("Unhandled exception (terminating=" + e.IsTerminating + "): " + ModLogger.Describe(e.ExceptionObject as Exception)); }
            catch { }
        }

        // Used for emergency logging
        internal static string GuessGameDirectory()
        {
            if (!string.IsNullOrEmpty(GameDirectory)) return GameDirectory;
            if (!string.IsNullOrEmpty(Preloader.GameDirectory)) return Preloader.GameDirectory;
            try
            {
                string location = typeof(ModLoader).Assembly.Location;
                if (!string.IsNullOrEmpty(location)) return Preloader.GuessGameDirectory(Path.GetDirectoryName(Path.GetFullPath(location)));
            }
            catch { }
            return Path.GetFullPath(".");
        }
    }
}
