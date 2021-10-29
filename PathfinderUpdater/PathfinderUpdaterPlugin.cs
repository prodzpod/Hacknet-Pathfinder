﻿using System;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Hacknet;
using BepInEx.Logging;
using Hacknet;
using Hacknet.Gui;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;
using Pathfinder.Event;
using Pathfinder.Event.Menu;
using Pathfinder.Event.Options;
using Pathfinder.GUI;
using Pathfinder.Options;
using Version = SemanticVersioning.Version;

namespace PathfinderUpdater
{
    [BepInPlugin(ModGUID, ModName, HacknetChainloader.VERSION)]
    [HarmonyPatch]
    public class PathfinderUpdaterPlugin : HacknetPlugin
    {
        public const string ModGUID = "com.Pathfinder.Updater";
        public const string ModName = "AutoUpdater";

        public const string PATHFINDER_RELEASE_URL = "https://api.github.com/repos/Arkhist/Hacknet-Pathfinder/releases";

        new internal static ConfigFile Config;

        internal static ConfigEntry<bool> IsEnabled;
        internal static ConfigEntry<bool> EnablePreReleases;
        internal static ConfigEntry<string> AcceptedUpdate;
        internal static ConfigEntry<string> CurrentVersion;

        public static Version VersionToRequest = null;

        public override bool Load()
        {
            Config = base.Config;

            IsEnabled = Config.Bind<bool>("AutoUpdater", "EnableAutoUpdates", true, "Enables or disables automatic updates for PathfinderAPI");
            EnablePreReleases = Config.Bind<bool>("AutoUpdater", "UsePreReleases", false, "Whether or not to automatically update to beta versions");
            AcceptedUpdate = Config.Bind<string>("AutoUpdater", "LatestAcceptedUpdate", "", "Used internally to keep track of whether you accepted the update or not");
            CurrentVersion = Config.Bind<string>("AutoUpdater", "CurrentVersion", HacknetChainloader.VERSION,
                "Used internally to keep track of version.\nIf you want to skip updating to a version but keep the updater on, set this manually to the latest verison.");

            HarmonyInstance.PatchAll(typeof(PathfinderUpdaterPlugin));
            if (Type.GetType("Mono.Runtime") != null)
            {
                HarmonyInstance.Patch(
                    AccessTools.Method(
                        typeof(ConfigurationManager), "OpenExeConfigurationInternal"),
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(PathfinderUpdaterPlugin), nameof(FixConfig))));
            }

            if (!IsEnabled.Value)
                return true;

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            Task.Run(async () => await LoadAsync());

            return true;
        }

        private async Task LoadAsync()
        {
            var pfapiPath = Directory.GetFiles(Paths.PluginPath, "PathfinderAPI.dll", SearchOption.AllDirectories)[0];

            using(var client = new HttpClient())
            using(var file = File.OpenWrite(pfapiPath))
            {
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("a", "1"));

                JArray releasesList;
                try
                {
                    var releaseRepoData = await client.GetAsync(PATHFINDER_RELEASE_URL);
                    var releaseDataContent = await releaseRepoData.Content.ReadAsStringAsync();
                    releasesList = JArray.Parse(releaseDataContent);
                }
                catch (Exception e)
                {
                    Log.Log(LogLevel.Error, e);
                    return;
                }

                Version tag = null;
                JToken release = null;
                foreach (var possibleRelease in releasesList)
                {
                    var possibleTag = Version.Parse(possibleRelease.Value<string>("tag_name").Substring(1));
                    if (possibleTag.PreRelease != null && !EnablePreReleases.Value)
                        continue;

                    tag = possibleTag;
                    release = possibleRelease;
                    break;
                }

                if (HacknetChainloader.Version.Major != tag.Major)
                    Log.LogWarning($"Latest version of Pathfinder is major {tag.Major}, which is higher than Chainloader version {HacknetChainloader.Version.Major}. Please update manually!");

                if (Version.Parse(CurrentVersion.Value) == tag)
                    return;

                if (tag.ToString() != AcceptedUpdate.Value)
                {
                    VersionToRequest = tag;
                    return;
                }

                var releaseData = await client.GetAsync(
                    release["assets"]
                    .First(x => x.Value<string>("name") == "Pathfinder.Release.zip")
                    .Value<string>("browser_download_url")
                );
                var releaseStream = await releaseData.Content.ReadAsStreamAsync();

                using(var archive = new ZipArchive(releaseStream))
                {
                    File.Delete(pfapiPath);
                    archive.GetEntry("BepInEx/plugins/PathfinderAPI.dll").Open().CopyTo(file);
                    file.Flush();
                }

                CurrentVersion.Value = tag.ToString();
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Program), nameof(Program.Main))]
        private static void WaitToPFAPI() => PFWrapper.PFAPILoaded();

        private static void FixConfig(out string exePath)
        {
            exePath = Paths.ExecutablePath;
        }
    }

    internal static class PFWrapper
    {
        private static OptionCheckbox IsEnabledBox = new OptionCheckbox("Enabled", "Enables the auto updater", PathfinderUpdaterPlugin.IsEnabled.Value);
        private static OptionCheckbox IncludePrerelease = new OptionCheckbox("IncludePreReleases", "Autoupdate to pre-releases", PathfinderUpdaterPlugin.EnablePreReleases.Value);
        private static PFButton AcceptVersion = new PFButton(760, 330, 120, 30, "Yes", new Color(102,255,127));
        private static PFButton DenyVersion = new PFButton(900, 330, 120, 30, "No", new Color(255,92,87));
        private static PFButton SkipVersion = new PFButton(1040, 330, 120, 30, "Skip", new Color(255, 255, 87));

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void PFAPILoaded()
        {
            OptionsManager.AddOption("Updater", IsEnabledBox);
            OptionsManager.AddOption("Updater", IncludePrerelease);
            EventManager<CustomOptionsSaveEvent>.AddHandler(OptionsSaved);
            EventManager<DrawMainMenuEvent>.AddHandler(OnDrawMainMenu);
        }

        internal static void OptionsSaved(CustomOptionsSaveEvent args)
        {
            PathfinderUpdaterPlugin.IsEnabled.Value = IsEnabledBox.Value;
            PathfinderUpdaterPlugin.EnablePreReleases.Value = IncludePrerelease.Value;
        }

        internal static void OnDrawMainMenu(MainMenuEvent args)
        {
            if (PathfinderUpdaterPlugin.VersionToRequest == null)
                return;

            GuiData.spriteBatch.Draw(Utils.white, new Rectangle(0, 0, GuiData.spriteBatch.GraphicsDevice.Viewport.Width, GuiData.spriteBatch.GraphicsDevice.Viewport.Height), new Color(0, 0, 0, 0.65f));
            GuiData.spriteBatch.Draw(Utils.white, new Rectangle(700, 250, 500, 300), Color.Black);
            TextItem.doLabel(new Vector2(750, 260), $"New Pathfinder Version {PathfinderUpdaterPlugin.VersionToRequest}", Color.White);
            TextItem.doSmallLabel(new Vector2(750, 300), "Do you want to update? Yes will close the game.", Color.White);
            if (AcceptVersion.Do())
            {
                PathfinderUpdaterPlugin.AcceptedUpdate.Value = PathfinderUpdaterPlugin.VersionToRequest.ToString();
                MusicManager.stop();
                Game1.threadsExiting = true;
                Game1.getSingleton().Exit();
            }
            else if (DenyVersion.Do())
            {
                PathfinderUpdaterPlugin.VersionToRequest = null;
                EventManager<DrawMainMenuEvent>.RemoveHandler(OnDrawMainMenu);
            }
            else if (SkipVersion.Do())
            {
                PathfinderUpdaterPlugin.CurrentVersion.Value = PathfinderUpdaterPlugin.VersionToRequest.ToString();
                PathfinderUpdaterPlugin.VersionToRequest = null;
                EventManager<DrawMainMenuEvent>.RemoveHandler(OnDrawMainMenu);
            }
        }
    }
}