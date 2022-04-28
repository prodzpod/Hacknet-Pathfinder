using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text;
using Hacknet;
using Pathfinder.Event;
using Pathfinder.Event.Loading;
using Pathfinder.Event.Gameplay;
using Microsoft.Xna.Framework;
using MonoMod.Cil;
using HarmonyLib;
using Mono.Cecil.Cil;
using Pathfinder.Util;

namespace Pathfinder.Executable
{
    [HarmonyPatch]
    public static class ExecutableManager
    {
        private struct CustomExeInfo
        {
            public string ExeData;
            public string XmlId;
            public Type ExeType;
        }

        private static readonly List<CustomExeInfo?> CustomExes = new List<CustomExeInfo?>();
        
        static ExecutableManager()
        {
            EventManager<TextReplaceEvent>.AddHandler(GetTextReplacementExe);
            EventManager<ExecutableExecuteEvent>.AddHandler(OnExeExecute);
            EventManager.onPluginUnload += OnPluginUnload;
        }

        private static void GetTextReplacementExe(TextReplaceEvent e)
        {
            var exe = CustomExes.FirstOrDefault(x => x.Value.XmlId == e.Original);
            if (!exe.HasValue)
                return;
            e.Replacement = exe.Value.ExeData;
        }
        private static void OnExeExecute(ExecutableExecuteEvent e)
        {
            var exe = CustomExes.FirstOrDefault(x => x.Value.ExeData == e.ExecutableData);
            if (!exe.HasValue)
                return;
            var location = new Rectangle(e.OS.ram.bounds.X, e.OS.ram.bounds.Y + RamModule.contentStartOffset, RamModule.MODULE_WIDTH, (int)OS.EXE_MODULE_HEIGHT);
            if(exe.Value.ExeType.IsSubclassOf(typeof(GameExecutable)))
                e.OS.AddGameExecutable(
                    (GameExecutable)Activator.CreateInstance(exe.Value.ExeType, null),
                    location,
                    e.Arguments.ToArray()
                );
            else
                e.OS.addExe((BaseExecutable)Activator.CreateInstance(exe.Value.ExeType, new object[] { location, e.OS, e.Arguments.ToArray() }));
            e.Result = ExecutionResult.StartupSuccess;
        }

        private static void OnPluginUnload(Assembly pluginAsm)
        {
            CustomExes.RemoveAll(x => x.Value.ExeType.Assembly == pluginAsm);
        }

        public static void RegisterExecutable<T>(string xmlName) where T : BaseExecutable => RegisterExecutable(typeof(T), xmlName);
        public static void RegisterExecutable(Type executableType, string xmlName)
        {
            executableType.ThrowNotInherit<BaseExecutable>(nameof(executableType));
            var builder = new StringBuilder();
            foreach (var exeByte in Encoding.ASCII.GetBytes("PathfinderExe:" + executableType.FullName))
                builder.Append(Convert.ToString(exeByte, 2));
            CustomExes.Add(new CustomExeInfo
            {
                ExeData = builder.ToString(),
                XmlId = xmlName,
                ExeType = executableType
            });
        }

        public static string GetCustomExeData(string xmlName) => CustomExes.FirstOrDefault(x => x.Value.XmlId == xmlName)?.ExeData;

        public static void UnregisterExecutable(string xmlName)
        {
            CustomExes.RemoveAll(x => x.Value.XmlId == xmlName);
        }
        public static void UnregisterExecutable<T>() => UnregisterExecutable(typeof(T));
        public static void UnregisterExecutable(Type exeType)
        {
            CustomExes.RemoveAll(x => x.Value.ExeType == exeType);
        }

        private static void AddGameExecutable(this OS os, GameExecutable exe, Rectangle location, string[] args)
        {
            exe.Assign(location, os, args);
            var computer = os.connectedComp ?? os.thisComputer;
            try
            {
                if(exe.needsProxyAccess && computer.proxyActive)
                {
                    exe.OnProxyBypassFailure();
                    if(!exe.IgnoreProxyFailPrint)
                        os.write(LocaleTerms.Loc("Proxy Active -- Cannot Execute"));
                    return;
                }

                if(os.ramAvaliable >= exe.ramCost)
                {
                    exe.OnInitialize();
                    if(exe.CanAddToSystem)
                        os.exes.Add(exe);
                    return;
                }

                exe.OnNoAvailableRam();
                if(!exe.IgnoreMemoryBehaviorPrint)
                {
                    os.ram.FlashMemoryWarning();
                    os.write(LocaleTerms.Loc("Insufficient Memory"));
                }
            }
            catch(Exception e)
            {
                if(exe.CatchException(e))
                    throw e;
            }
        }

        [HarmonyILManipulator]
        [HarmonyPatch(typeof(OS), nameof(OS.Update), typeof(GameTime), typeof(bool), typeof(bool))]
        private static void onOSUpdate(ILContext il)
        {
            var c = new ILCursor(il);

            c.GotoNext(MoveType.Before,
                x => x.MatchNop(),
                x => x.MatchLdarg(0),
                x => x.MatchLdfld(AccessTools.Field(typeof(OS), nameof(OS.exes))),
                x => x.MatchLdloc(1),
                x => x.MatchCallvirt(AccessTools.Method(typeof(List<ExeModule>), nameof(List<ExeModule>.RemoveAt), new[] {typeof(int)}))
            );

            c.Emit(OpCodes.Ldarg, 0);
            c.Emit(OpCodes.Ldloc, 1);

            c.EmitDelegate<Action<OS, int>>((os, index) =>
            {
                if(os.exes[index] is GameExecutable exe)
                    exe.Completed();
            });
        }

        [HarmonyILManipulator]
        [HarmonyPatch(typeof(Programs), nameof(Programs.execute), typeof(string[]), typeof(OS))]
        private static void onExeCommandRun(ILContext il)
        {
            var c = new ILCursor(il);
            c.GotoNext(MoveType.After,
                x => x.MatchLdarg(1),
                x => x.MatchLdfld(AccessTools.Field(typeof(OS), nameof(OS.thisComputer))),
                x => x.MatchLdfld(AccessTools.Field(typeof(Computer), nameof(Computer.files))),
                x => x.MatchLdfld(AccessTools.Field(typeof(FileSystem), nameof(FileSystem.root)))
            );
            c.RemoveRange(3); // removed .files[2]
            c.Emit(OpCodes.Ldstr, "bin"); // replace with .searchForFolder("bin") for mkdir mods
            c.Emit(OpCodes.Callvirt, AccessTools.Method(typeof(Folder), nameof(Folder.searchForFolder), new[] { typeof(string) }));

            c.GotoNext(MoveType.Before,
                x => x.MatchLdcI4(0),
                x => x.MatchStloc(2)
            );
            int start = c.Index;
            c.GotoNext(MoveType.Before,
                x => x.MatchLdarg(1),
                x => x.MatchLdstr(" ")
            );
            int end = c.Index;
            c.Goto(start);
            c.RemoveRange(end - start); // remove the whole loop
            c.Emit(OpCodes.Ldarg_1);
            c.Emit(OpCodes.Ldloc_1); // printCommands(os, folder)
            c.EmitDelegate<Action<OS, Folder>>((os, folder) =>
            {
                foreach (FileEntry file in folder.files)
                {
                    if (PortExploits.crackExeData.Values.Contains(file.data)) os.write(file.name.Replace(".exe", ""));
                    else if (PortExploits.crackExeDataLocalRNG.Values.Contains(file.data)) os.write(file.name.Replace(".exe", ""));
                    else foreach (CustomExeInfo info in CustomExes) if (info.ExeData.Equals(file.data)) os.write(file.name.Replace(".exe", ""));
                }
            });
        }
    }
}
