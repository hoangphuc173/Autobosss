using Xunit;
using AutoBossShared;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace AutoBoss.Tests;

[Collection("PluginEnv")]
public class IpcMessageTests
{
    [Fact]
    public void Serialize_UsesCamelCaseContract()
    {
        var msg = new IpcMessage(MessageTypes.COMMAND);
        msg.Payload["command"] = Commands.START_FARMING;

        var json = JsonConvert.SerializeObject(msg);

        Assert.Contains("\"type\":\"COMMAND\"", json);
        Assert.Contains("\"payload\":", json);
    }

    [Fact]
    public void JsonRoundTrip_PreservesTypeAndPayload()
    {
        var msg = new IpcMessage(MessageTypes.COMMAND)
        {
            Payload = new Dictionary<string, object>
            {
                ["command"] = Commands.TELEPORT_TO_MAP,
                ["targetMap"] = "Cung",
            }
        };

        var clone = JsonConvert.DeserializeObject<IpcMessage>(JsonConvert.SerializeObject(msg));

        Assert.Equal(MessageTypes.COMMAND, clone!.Type);
        Assert.Equal(Commands.TELEPORT_TO_MAP, clone.Payload["command"].ToString());
        Assert.Equal("Cung", clone.Payload["targetMap"].ToString());
    }

    [Fact]
    public void MessageTypes_And_Commands_AreConsistent()
    {
        // Guard: cac ten message/command duoc ca 2 ben dung chuoi literal,
        // nen test nay phong truong hop ai doi ten lam vo IPC.
        Assert.Equal("HEARTBEAT", MessageTypes.HEARTBEAT);
        Assert.Equal("STATUS_UPDATE", MessageTypes.STATUS_UPDATE);
        Assert.Equal("COMMAND", MessageTypes.COMMAND);
        Assert.Equal("CONFIG_UPDATE", MessageTypes.CONFIG_UPDATE);
        Assert.Equal("SHUTDOWN", MessageTypes.SHUTDOWN);

        Assert.Equal("TELEPORT_TO_MAP", Commands.TELEPORT_TO_MAP);
        Assert.Equal("SWITCH_ZONE", Commands.SWITCH_ZONE);
        Assert.Equal("INVALIDATE_CACHE", Commands.INVALIDATE_CACHE);
    }

    [Fact]
    public void IpcConfig_DefaultPort_Is28081()
    {
        // Ca Manager (server) va plugin (client) phai cung port.
        Assert.Equal(28081, IpcConfig.ServerPort);
    }
}

[Collection("PluginEnv")]
public class BotProfileTests
{
    [Fact]
    public void DefaultProfile_PassesValidation()
    {
        // Range phai khop voi ProfileManager.ValidateProfile cua Manager
        // (test project khong reference truc tiep WPF app nen kiem tra range tai day).
        var profile = new BotProfile();

        Assert.InRange(profile.AttackRange, 0.3f, 50f);
        Assert.InRange(profile.LootRadius, 0.5f, 1000f);
        Assert.InRange(profile.CombatTimeoutSec, 5f, 300f);
        Assert.InRange(profile.RetreatHpPct, 10f, 100f);
        Assert.InRange(profile.MaxZoneAttempts, 1, 50);
    }

    [Fact]
    public void JsonRoundTrip_SkillTrigger_CamelCase()
    {
        var profile = new BotProfile();
        profile.BossSkillTriggers.Add(new SkillTrigger { HpThreshold = 500000f, SkillKey = 2, SpamCount = 3 });

        var json = JsonConvert.SerializeObject(profile);
        var clone = JsonConvert.DeserializeObject<BotProfile>(json)!;

        var t = Assert.Single(clone.BossSkillTriggers);
        Assert.Equal(500000f, t.HpThreshold);
        Assert.Equal(2, t.SkillKey);
        Assert.Equal(3, t.SpamCount);
    }

    [Fact]
    public void SkillTrigger_DeserializesFromManagerPayload()
    {
        // Manager gui payload key camelCase (JsonProperty cua Shared).
        const string json = @"{""hpThreshold"":50000,""skillKey"":3,""spamCount"":1}";

        var trigger = JsonConvert.DeserializeObject<SkillTrigger>(json);

        Assert.NotNull(trigger);
        Assert.Equal(50000f, trigger.HpThreshold);
        Assert.Equal(3, trigger.SkillKey);
    }
}

[Collection("PluginEnv")]
public class BotInstanceStateTests
{
    [Fact]
    public void NewInstance_HasSaneDefaults()
    {
        var s = new BotInstanceState();

        Assert.NotEqual(Guid.Empty, s.InstanceId);
        Assert.Equal(ConnectionStatus.Disconnected, s.Status);
        Assert.Equal(AutoBossState.Idle, s.CurrentState);
        Assert.Equal(0, s.BossKillsThisSession);
    }

    [Fact]
    public void Uptime_GrowsFromSessionStart()
    {
        var s = new BotInstanceState { SessionStartTime = DateTime.Now.AddHours(-2) };

        Assert.True(s.Uptime.TotalHours >= 1.9);
    }

    [Fact]
    public void KillsPerHour_NoDivideByZero()
    {
        var s = new BotInstanceState
        {
            SessionStartTime = DateTime.Now,
            BossKillsThisSession = 10,
        };

        Assert.True(s.BossKillsPerHour > 0); // phai tra ve so hop ly, khong NaN/Infinity
        Assert.False(double.IsInfinity(s.BossKillsPerHour));
    }
}
