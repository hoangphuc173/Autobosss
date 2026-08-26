using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AutoBossLauncher
{
    public class AccountData
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("username")]
        public string Username { get; set; } = "";

        [JsonPropertyName("password")]
        public string Password { get; set; } = "";

        [JsonPropertyName("server")]
        public int Server { get; set; } = 0;

        [JsonPropertyName("character")]
        public int Character { get; set; } = 1;

        [JsonPropertyName("headless")]
        public bool Headless { get; set; } = true;

        [JsonPropertyName("autoLogin")]
        public bool AutoLogin { get; set; } = true;

        [JsonPropertyName("autoHunting")]
        public bool AutoHunting { get; set; } = true;

        [JsonPropertyName("targetZone")]
        public int TargetZone { get; set; } = 8;

        [JsonPropertyName("profileIndex")]
        public int ProfileIndex { get; set; } = 0;

        [JsonPropertyName("running")]
        public bool Running { get; set; } = false;
    }

    public class SettingsData
    {
        [JsonPropertyName("windowWidth")]
        public int WindowWidth { get; set; } = 1000;

        [JsonPropertyName("windowHeight")]
        public int WindowHeight { get; set; } = 800;

        [JsonPropertyName("disableShadows")]
        public bool DisableShadows { get; set; } = true;

        [JsonPropertyName("disableParticles")]
        public bool DisableParticles { get; set; } = true;

        [JsonPropertyName("lowQuality")]
        public bool LowQuality { get; set; } = true;

        [JsonPropertyName("targetFps")]
        public int TargetFps { get; set; } = 60;

        [JsonPropertyName("minimizeOnLaunch")]
        public bool MinimizeOnLaunch { get; set; } = true;

        [JsonPropertyName("gameSpeed")]
        public int GameSpeed { get; set; } = 1;

        [JsonPropertyName("autoCleanRAM")]
        public bool AutoCleanRAM { get; set; } = true;

        [JsonPropertyName("ultraLowRes")]
        public bool UltraLowRes { get; set; } = true;

        [JsonPropertyName("liteMode")]
        public bool LiteMode { get; set; } = true;
    }

    public class AccountsFile
    {
        [JsonPropertyName("accounts")]
        public List<AccountData> Accounts { get; set; } = new List<AccountData>();

        [JsonPropertyName("currentAccountIndex")]
        public int CurrentAccountIndex { get; set; } = 0;

        [JsonPropertyName("settings")]
        public SettingsData Settings { get; set; } = new SettingsData();
    }
}
