using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Playdex
{
    public class Playdex : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        
        private PlaydexSettingsViewModel settings { get; set; }

        private PlaySessionClient playSessionClient { get; set; }

        public override Guid Id { get; } = Guid.Parse("65b45bc8-3d5d-4ff8-9d11-9e4027d1926d");

        public Playdex(IPlayniteAPI api) : base(api)
        {
            settings = new PlaydexSettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };
            playSessionClient = new PlaySessionClient(settings.Settings.ApiKey);
        }

        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            // Add code to be executed when game is finished installing.
        }

        public override async void OnGameStarted(OnGameStartedEventArgs args)
        {
            try
            {
                var url = args.Game.Links.FirstOrDefault(link => link.Name.ToLower().Equals("steam"));
                var provider = "unknown";
                string steamId = null;

                if (url != null)
                {
                    steamId = SteamUrl.TryGetSteamAppId(url.Url).ToString();
                    provider = "steam";
                }
                
                await playSessionClient.StartSessionAsync(args.Game.Name, steamId, provider);
            }
            catch (Exception e)
            {
                logger.Error(e, $"playdex: Error while trying to start session {args.Game.Name}");
            }
        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            // Add code to be executed when game is preparing to be started.
        }

        public override async void OnGameStopped(OnGameStoppedEventArgs args)
        {
            try
            {
                await playSessionClient.EndSessionAsync();
            }
            catch (Exception e)
            {
                logger.Error(e, $"playdex: Error while trying to end session {args.Game.Name},{args.ElapsedSeconds}");
            }
        }

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        {
            // Add code to be executed when game is uninstalled.
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            // Add code to be executed when Playnite is initialized.
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            // Add code to be executed when Playnite is shutting down.
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            // Add code to be executed when library is updated.
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new PlaydexSettingsView();
        }
    }
}