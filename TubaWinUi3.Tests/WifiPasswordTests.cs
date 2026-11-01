using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class WifiPasswordTests
{
    private static string Profile(string auth, string cipher, string key = "", bool protect = false) => $"""
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
          <name>配置文件</name><SSIDConfig><SSID><name>中文 WiFi</name></SSID><nonBroadcast>true</nonBroadcast></SSIDConfig>
          <connectionMode>auto</connectionMode><MSM><security><authEncryption><authentication>{auth}</authentication><encryption>{cipher}</encryption></authEncryption>
          <sharedKey><protected>{protect.ToString().ToLowerInvariant()}</protected><keyMaterial>{key}</keyMaterial></sharedKey>
          </security></MSM>
        </WLANProfile>
        """;

    [Fact] public void ReadsChineseProfileAndConnectionPreferences()
    {
        var n = WifiPasswordService.ParseProfile(Profile("WPA2PSK", "AES", "a&amp;b:;"));
        Assert.Equal("中文 WiFi", n.Ssid);
        Assert.Equal("a&b:;", n.Password);
        Assert.True(n.HasPassword);
        Assert.True(n.Hidden);
        Assert.True(n.AutoConnect);
        Assert.False(n.IsOpen);
    }
    [Fact] public void ProtectedKeyIsNeverDisplayedAsPassword()
    {
        var n = WifiPasswordService.ParseProfile(Profile("WPA2PSK", "AES", "encrypted", true));
        Assert.False(n.HasPassword);
        Assert.Empty(n.Password);
        Assert.False(n.IsOpen);
        Assert.False(n.CanShare);
    }
    [Fact] public void EnterpriseWithoutKeyIsNotOpen()
    {
        var n = WifiPasswordService.ParseProfile(Profile("WPA2", "AES"));
        Assert.False(n.IsOpen);
        Assert.False(n.CanShare);
    }
    [Fact] public void OpenNetworkSharesWithoutKey()
    {
        var n = WifiPasswordService.ParseProfile(Profile("open", "none"));
        Assert.True(n.IsOpen);
        Assert.Equal("WIFI:T:nopass;S:中文 WiFi;P:;H:true;;", n.QrPayload());
    }
    [Fact] public void QrEscapesReservedCharacters()
    {
        var n = new WifiNetwork { Ssid = "a;b,c:d\\e\"f", Password = "p;\\", HasPassword = true, Authentication = "WPA2PSK" };
        Assert.Equal("WIFI:T:WPA;S:a\\;b\\,c\\:d\\\\e\\\"f;P:p\\;\\\\;H:false;;", n.QrPayload());
    }
    [Fact] public void SaeDoesNotProduceIncompatibleWpaQr()
    {
        var n = WifiPasswordService.ParseProfile(Profile("WPA3SAE", "AES", "password"));
        Assert.False(n.CanShare);
        Assert.Throws<InvalidOperationException>(() => n.QrPayload());
    }
}
