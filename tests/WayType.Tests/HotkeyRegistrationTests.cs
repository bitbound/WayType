using System.Text.Json;
using WayType.Libraries.Core.Serialization;
using WayType.Libraries.Portal;

namespace WayType.Tests;

public class HotkeyRegistrationTests
{
    [Fact]
    public void Deserialize_WhenFileIsTheOldFormatWithoutATrigger_ReadsTheIdsAndLeavesTheTriggerNull()
    {
        var registration = JsonSerializer.Deserialize<HotkeyRegistration>(
            """{"shortcutIds":["waytype_hotkey_ae6bb9db"]}""",
            WayTypeJson.Options);

        Assert.NotNull(registration);
        Assert.Equal(["waytype_hotkey_ae6bb9db"], registration.ShortcutIds);
        Assert.Null(registration.Trigger);
    }

    [Fact]
    public void RoundTrip_WhenTriggerIsStored_SurvivesSoARebindCanTellAChangeFromARestart()
    {
        var json = JsonSerializer.Serialize(new HotkeyRegistration(["waytype_hotkey_ae6bb9db"], "CTRL+SHIFT+D"), WayTypeJson.Options);

        var registration = JsonSerializer.Deserialize<HotkeyRegistration>(json, WayTypeJson.Options);

        Assert.Equal("CTRL+SHIFT+D", registration!.Trigger);
    }
}
